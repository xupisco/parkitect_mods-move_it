using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Parkitect.UI;
using UnityEngine;

namespace move_it
{
    // The local player's move in progress: from the click on the object until the placement is confirmed or cancelled.
    // Nothing here runs on other clients, so it only affects the preview.
    internal sealed class move_session
    {
        private static readonly AccessTools.FieldRef<Builder, Quaternion> builder_rotation =
            AccessTools.FieldRefAccess<Builder, Quaternion>("rotation");
        private static readonly AccessTools.FieldRef<Builder, Quaternion> builder_ghost_forward_rotation =
            AccessTools.FieldRefAccess<Builder, Quaternion>("ghostForwardRotation");
        private static readonly AccessTools.FieldRef<Builder, bool> builder_ghost_changed =
            AccessTools.FieldRefAccess<Builder, bool>("ghostChanged");
        private static readonly AccessTools.FieldRef<Builder, bool> builder_only_build_one =
            AccessTools.FieldRefAccess<Builder, bool>("onlyBuildOne");
        private static readonly MethodInfo builder_instantiate_ghost =
            AccessTools.Method(typeof(Builder), "instantiateGhost");

        private const int blueprint_header_length = 23;
        // BlueprintBuilder wants a file, but the preview patch hands it the data in memory instead of opening this.
        private const string group_file_path = "move_it_group";
        // Enough to tell a fine-tuned height from a step, and to absorb the error of reading it back through a ray.
        private const float height_tolerance = 0.001f;
        private const float tooltip_interval = 0.05f;
        // Lined up under the builder's price tag, which sits just below and to the right of the cursor.
        private static readonly Vector2 tooltip_offset = new Vector2(27f, -34f);

        private static move_session active_session;

        public readonly Builder builder;
        public readonly BuildableObject original;
        public readonly string reference_name;
        public readonly int variant;
        public readonly float tower_height = -1f;
        // The height of a deco left between the game's height steps, as Transform Anarchy can do. NaN for any other.
        private readonly float fine_height = float.NaN;
        public readonly List<dependent> dependents = new List<dependent>();
        // When moving only an entrance or exit: the station it must stay at.
        public readonly StationController station;

        // When moving a group: what was selected, everything serialized with it, and that serialized data.
        public readonly List<BuildableObject> group_roots;
        public readonly List<BuildableObject> group_members;
        public readonly byte[] blueprint_data;

        // The original and everything that goes away with it, like a ride's platforms, entrance and exit.
        private readonly HashSet<BuildableObject> parts = new HashSet<BuildableObject>();
        private bool confirmed;
        private bool copy_sent;
        private float tooltip_timeout;
        private bool only_build_one_before_copying;

        public bool is_group => blueprint_data != null;
        public bool is_copying { get; private set; }
        // Not a lone entrance or exit, which belongs to its station, or a group, whose blueprint needs its own command.
        private bool can_copy => !is_group && move_rules.can_copy(original);

        // A ride's entrance or exit, placed relative to the ride so that it follows the ghost.
        internal sealed class dependent
        {
            public EntranceExit original;
            public string reference_name;
            public Vector3 local_position;
            public Quaternion local_rotation;
        }

        // A builder can be destroyed without raising OnCancelled, for example when another window replaces it.
        public static move_session current
        {
            get
            {
                if (active_session != null && active_session.builder == null)
                {
                    active_session.close();
                }
                return active_session;
            }
        }

        private move_session(Builder builder, BuildableObject original)
        {
            this.builder = builder;
            this.original = original;
            reference_name = original.getReferenceName();
            variant = object_state.get_variant(original);
            if (builder is DecoBuilder && original is Deco deco)
            {
                fine_height = get_fine_height(deco);
            }
            if (original is TowerRide tower_ride)
            {
                tower_height = tower_ride.getHeight();
            }
            if (original is EntranceExit original_entrance_exit)
            {
                station = original_entrance_exit.connectedAttractionPlatform.getStationController();
            }

            List<SerializedMonoBehaviour> belonging = new List<SerializedMonoBehaviour>();
            original.retrieveObjectsBelongingToThis(belonging);
            Quaternion inverse_rotation = Quaternion.Inverse(original.logicTransform.rotation);
            foreach (SerializedMonoBehaviour item in belonging)
            {
                if (item is BuildableObject part && parts.Add(part) && part != original && part is EntranceExit entrance_exit)
                {
                    dependents.Add(new dependent
                    {
                        original = entrance_exit,
                        reference_name = entrance_exit.getReferenceName(),
                        local_position = inverse_rotation * (entrance_exit.transform.position - original.logicTransform.position),
                        local_rotation = inverse_rotation * entrance_exit.transform.rotation
                    });
                }
            }
        }

        private move_session(Builder builder, List<BuildableObject> roots, List<BuildableObject> members, byte[] data)
        {
            this.builder = builder;
            original = roots[0];
            variant = -1;
            group_roots = roots;
            group_members = members;
            blueprint_data = data;
            foreach (BuildableObject member in members)
            {
                parts.Add(member);
            }
        }

        public bool is_part_of_original(BuildableObject buildable_object)
        {
            return buildable_object != null && parts.Contains(buildable_object);
        }

        // Deco stuck to a surface is left out: it isn't rounded while it sticks, so its height is no sign of fine-tuning.
        private static float get_fine_height(Deco deco)
        {
            float step = deco.heightChangeDelta;
            if (step <= 0f || deco.snapToDefaultHeightWhenStickingToSurface)
            {
                return float.NaN;
            }
            float height = deco.logicTransform.position.y;
            float nearest_step = Mathf.Round(height / step) * step;
            return Mathf.Abs(height - nearest_step) > height_tolerance ? height : float.NaN;
        }

        // Only the height the deco came with: anything else, like dragging with the height key, follows the game's steps.
        public bool keeps_fine_height(float height)
        {
            return Mathf.Abs(height - fine_height) < height_tolerance;
        }

        // What the builder's ghost objects are compared with: the original and the entrance and exit shown with it.
        public float get_original_price()
        {
            if (is_group)
            {
                float group_price = 0f;
                foreach (BuildableObject member in group_members)
                {
                    if (member != null)
                    {
                        group_price += member.getRealPrice();
                    }
                }
                return group_price;
            }

            float price = original != null ? original.getRealPrice() : 0f;
            foreach (dependent item in dependents)
            {
                if (item.original != null)
                {
                    price += item.original.getRealPrice();
                }
            }
            return price;
        }

        public static void begin(BuildableObject original, bool start_copying)
        {
            current?.cancel();

            // Always the prefab: the builder clones what it is given, and the original holds runtime state.
            BuildableObject prefab = ScriptableSingleton<AssetManager>.Instance.getPrefab<BuildableObject>(original.getReferenceName());
            Builder builder = instantiate_builder(original, prefab);
            active_session = new move_session(builder, original);

            Quaternion rotation = original.logicTransform.rotation;
            builder.snapRotation(rotation * Vector3.forward);
            builder_rotation(builder) = rotation;
            builder_ghost_forward_rotation(builder) = rotation;
            if (float.IsNaN(active_session.fine_height))
            {
                builder.setFixedGhostHeightIfRaised(original.logicTransform.position);
            }
            else
            {
                // Even close to the ground, where the builder would otherwise put it back on the terrain.
                builder.setFixedGhostHeight(active_session.fine_height);
            }
            builder.copySettingsFrom(original);
            active_session.subscribe();

            // The same as pressing Tab right away. toggle_copy_mode leaves objects that can't be copied to move.
            if (start_copying)
            {
                active_session.toggle_copy_mode();
            }
        }

        public static void begin_group(List<BuildableObject> roots)
        {
            current?.cancel();

            // The same list and order the native blueprint tool saves, so that the objects can be matched up again
            // when they are rebuilt. Only what gets serialized counts, like RideBlueprintSupport in Undo Redo Undelete.
            List<SerializedMonoBehaviour> selected = new List<SerializedMonoBehaviour>(roots);
            List<SerializedMonoBehaviour> serialized = new List<SerializedMonoBehaviour>();
            List<BuildableObject> members = new List<BuildableObject>();
            foreach (SerializedMonoBehaviour item in BlueprintCreationUtility.collectBlueprintedObjects(selected))
            {
                if (item != null && item.canBeSerialized())
                {
                    serialized.Add(item);
                    if (item is BuildableObject member)
                    {
                        members.Add(member);
                    }
                }
            }

            byte[] data = serialize_blueprint(serialized);
            if (data == null)
            {
                Debug.LogError("[move_it] Couldn't serialize the selected group");
                return;
            }

            BlueprintBuilder builder = Object.Instantiate(ScriptableSingleton<AssetManager>.Instance.blueprintBuilderGO);
            builder.skipDeco = false;
            builder.filePath = group_file_path;
            active_session = new move_session(builder, roots, members, data);

            // The builder aims at the terrain, which would otherwise bring a raised group down to the ground.
            builder.setFixedGhostHeightIfRaised(BlueprintBuilder.getPivot(serialized));
            active_session.subscribe();
        }

        // Without BlueprintSerializer's header, which is what BlueprintBuildCommand expects: the two "SM" bytes,
        // a version byte, the 4-byte length and the 16-byte MD5 hash.
        private static byte[] serialize_blueprint(List<SerializedMonoBehaviour> serialized)
        {
            byte[] blueprint = new BlueprintSerializer(serialized, "Move It").getSerialized();
            if (blueprint == null || blueprint.Length <= blueprint_header_length || blueprint[0] != 'S' || blueprint[1] != 'M')
            {
                return null;
            }
            byte[] data = new byte[blueprint.Length - blueprint_header_length];
            System.Buffer.BlockCopy(blueprint, blueprint_header_length, data, 0, data.Length);
            return data;
        }

        private void subscribe()
        {
            builder.OnBuildTriggered += on_build_triggered;
            builder.OnCancelled += on_cancelled;
            foreach (BuildableObject part in parts)
            {
                part.OnKilled += on_part_killed;
            }
        }

        // An entrance or exit needs its ride's own builder, which knows where it can be placed.
        private static Builder instantiate_builder(BuildableObject original, BuildableObject prefab)
        {
            if (original is Entrance entrance)
            {
                return entrance.attraction.getNewEntranceBuilderInstance();
            }
            if (original is Exit exit)
            {
                return exit.attraction.getNewExitBuilderInstance();
            }
            return prefab.instantiateBuilder();
        }

        public static bool is_builder(Builder builder)
        {
            move_session session = current;
            return session != null && session.builder == builder;
        }

        public void mark_confirmed()
        {
            confirmed = true;
        }

        public void mark_copy_sent()
        {
            copy_sent = true;
        }

        public void toggle_copy_mode()
        {
            if (!can_copy)
            {
                return;
            }

            is_copying = !is_copying;
            // Shop and ride builders stop after one build. Copies go on until Esc, as they do for deco.
            if (is_copying)
            {
                only_build_one_before_copying = builder_only_build_one(builder);
                builder_only_build_one(builder) = false;
            }
            else
            {
                builder_only_build_one(builder) = only_build_one_before_copying;
            }

            // A copy is a new object, so the ghost shows neither the ride's entrance and exit nor the tower's height.
            if (dependents.Count > 0 || tower_height >= 0f)
            {
                builder_instantiate_ghost.Invoke(builder, null);
            }
            // The price and the collision check depend on the mode, but the builder only rechecks them when it has to.
            builder_ghost_changed(builder) = true;
        }

        // Says whether the object in hand is being moved or copied, for as long as the session lasts. Refreshed the way
        // AbstractPipetteTool refreshes its own, so that it fades out soon after the session ends.
        public void tick_action_tooltip()
        {
            if (tooltip_timeout <= 0f)
            {
                tooltip_timeout = tooltip_interval;
                string action = is_copying ? texts.copy_action : texts.move_action;
                UITooltipController.Instance.showTooltip(action, instantly: true, tooltip_interval * 1.1f, tooltip_offset);
            }
            tooltip_timeout -= Time.unscaledDeltaTime;
        }

        public void cancel()
        {
            if (builder != null)
            {
                // Raises OnCancelled, which closes the session.
                builder.cancelBuilding();
            }
            else
            {
                close();
            }
        }

        private void on_build_triggered()
        {
            // The builder stays open for more copies, until Esc or a normal click that moves the original.
            if (copy_sent)
            {
                copy_sent = false;
                return;
            }

            close();
            // The native builders keep placing copies until told to stop. Destroy is deferred to the end of the
            // frame, so the rest of Builder.buildObjects can still run safely.
            builder.cancelBuilding();
        }

        private void on_cancelled()
        {
            close();
        }

        // Right click deletes objects while a builder is open, and in multiplayer another player can remove them too.
        private void on_part_killed(SerializedMonoBehaviour killed_object)
        {
            if (!confirmed)
            {
                cancel();
            }
        }

        private void close()
        {
            if (active_session == this)
            {
                active_session = null;
            }
            if (builder != null)
            {
                builder.OnBuildTriggered -= on_build_triggered;
                builder.OnCancelled -= on_cancelled;
            }
            foreach (BuildableObject part in parts)
            {
                if (part != null)
                {
                    part.OnKilled -= on_part_killed;
                }
            }
        }
    }
}
