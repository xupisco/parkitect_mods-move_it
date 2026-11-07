using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace move_it
{
    // The whole move travels as one ordered command and runs atomically on every client, so that another player's
    // build can't land between the removal and the rebuild.
    internal static class move_command
    {
        public const string command_id = move_it_mod.identifier + "/move";

        private const byte kind_deco = 0;
        private const byte kind_path_attachment = 1;
        private const byte kind_block = 2;
        private const byte kind_flat_ride = 3;
        private const byte kind_entrance_exit = 4;

        private const byte payload_single = 0;
        private const byte payload_group = 1;
        private const byte payload_copy = 2;

        // Non-null only while on_received is running. The patches use it to adjust the native build.
        public static execution current_execution { get; private set; }

        internal sealed class execution
        {
            public readonly int variant;
            public float original_price;
            public BuildableObject built_object;
            // In a group, one blueprint command builds everything: the prices of the originals, in build order.
            public List<float> group_prices;
            public readonly List<BuildableObject> group_built = new List<BuildableObject>();

            public execution(int variant)
            {
                this.variant = variant;
            }

            // Called once per object, as the native build charges for it.
            public float on_built(BuildableObject built)
            {
                built_object = built;
                if (group_prices == null)
                {
                    return original_price;
                }
                int index = group_built.Count;
                group_built.Add(built);
                return index < group_prices.Count ? group_prices[index] : 0f;
            }
        }

        // A ride's entrance or exit, rebuilt once the ride exists because its command needs the new ride's id.
        private sealed class dependent_build
        {
            public uint previous_id;
            public string reference_name;
            public Vector3 position;
            public Vector3 forward;
            public float original_price;
            public object_state state;
        }

        // A move costs only what the object became worth on top of what it was. Without this, it would pay to enlarge
        // a deco or raise a shop during the move and then sell it, since the resale value follows the current price.
        public static float get_move_cost(float new_price, float original_price)
        {
            return Mathf.Max(0f, new_price - original_price);
        }

        // Called with the commands the native builder was about to send.
        public static void send(move_session session, AbstractBaseBuildCommand build_command, bool copy)
        {
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(copy ? payload_copy : payload_single);
                writer.Write(session.original.objectID);
                writer.Write(get_own_player_id());
                write_build_command(writer, session, build_command);
                writer.Flush();
                data = stream.ToArray();
            }

            // After a copy the session goes on, and still has to end if the original is removed.
            if (!copy)
            {
                session.mark_confirmed();
            }
            CommandController.Instance.addCustomCommand(command_id, data);
        }

        public static void send_group(move_session session, Vector3 position, Vector3 forward)
        {
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(payload_group);
                writer.Write(get_own_player_id());
                write_object_ids(writer, session.group_roots);
                write_object_ids(writer, session.group_members);
                write_vector(writer, position);
                write_vector(writer, forward);
                writer.Write(session.blueprint_data.Length);
                writer.Write(session.blueprint_data);
                writer.Flush();
                data = stream.ToArray();
            }

            session.mark_confirmed();
            CommandController.Instance.addCustomCommand(command_id, data);
        }

        private static void write_object_ids(BinaryWriter writer, List<BuildableObject> objects)
        {
            writer.Write(objects.Count);
            foreach (BuildableObject item in objects)
            {
                writer.Write(item.objectID);
            }
        }

        // Null if any of them is gone, because a group moves as a whole or not at all.
        private static List<BuildableObject> read_objects(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            List<BuildableObject> objects = new List<BuildableObject>(count);
            bool all_found = true;
            for (int i = 0; i < count; i++)
            {
                BuildableObject item = GameController.Instance.getSerializedObject<BuildableObject>(reader.ReadUInt32());
                all_found &= item != null;
                objects.Add(item);
            }
            return all_found ? objects : null;
        }

        private static void receive_group(BinaryReader reader)
        {
            byte sender_player_id = reader.ReadByte();
            List<BuildableObject> roots = read_objects(reader);
            List<BuildableObject> members = read_objects(reader);
            Vector3 position = read_vector(reader);
            Vector3 forward = read_vector(reader);
            byte[] blueprint_data = reader.ReadBytes(reader.ReadInt32());

            // As in on_received, these checks only read synchronized state.
            if (roots == null || members == null)
            {
                return;
            }
            foreach (BuildableObject root in roots)
            {
                if (move_rules.get_group_refusal_reason(root) != null)
                {
                    return;
                }
            }

            List<object_state> states = new List<object_state>(members.Count);
            List<string> reference_names = new List<string>(members.Count);
            List<float> prices = new List<float>(members.Count);
            foreach (BuildableObject member in members)
            {
                states.Add(object_state.capture(member));
                reference_names.Add(member.getReferenceName());
                prices.Add(member.getRealPrice());
            }

            foreach (BuildableObject root in roots)
            {
                set_refundable(root, false);
                // Carries on if this ever fails: a duplicate is better than losing the rest of the group.
                if (!root.destruct())
                {
                    set_refundable(root, true);
                    Debug.LogError("[move_it] Couldn't remove " + root.getReferenceName() + " from the moved group");
                }
            }

            execution current = new execution(-1);
            current.group_prices = prices;
            current_execution = current;
            try
            {
                BlueprintBuildCommand command = new BlueprintBuildCommand(blueprint_data, position, forward, default(LoadOptions));
                run_build(command, current, 0f, get_own_player_id() == sender_player_id);
            }
            finally
            {
                current_execution = null;
            }

            if (current.group_built.Count != members.Count)
            {
                Debug.LogError("[move_it] Moved a group of " + members.Count + " objects but " + current.group_built.Count + " were rebuilt");
            }
            // The blueprint keeps the order they were saved in. The name check means that if it ever doesn't, an object
            // loses its state instead of getting another one's stock and history.
            int count = Mathf.Min(members.Count, current.group_built.Count);
            for (int i = 0; i < count; i++)
            {
                BuildableObject built = current.group_built[i];
                if (built.getReferenceName() != reference_names[i])
                {
                    Debug.LogError("[move_it] Group object " + i + " was rebuilt as " + built.getReferenceName() + " instead of " + reference_names[i]);
                    continue;
                }
                states[i].apply_to(built);
            }
        }

        public static void on_received(string identifier, byte[] data)
        {
            byte payload;
            uint object_id;
            byte sender_player_id;
            AbstractBaseBuildCommand build_command;
            List<dependent_build> dependents = new List<dependent_build>();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
            {
                payload = reader.ReadByte();
                if (payload == payload_group)
                {
                    receive_group(reader);
                    return;
                }
                object_id = reader.ReadUInt32();
                sender_player_id = reader.ReadByte();
                build_command = read_build_command(reader, dependents);
            }

            // The command runs later than it was sent in multiplayer, so all of this can have changed meanwhile.
            // These checks only read synchronized state, so every client reaches the same decision.
            BuildableObject original = GameController.Instance.getSerializedObject<BuildableObject>(object_id);
            if (original == null || build_command == null || move_rules.get_refusal_reason(original) != null)
            {
                return;
            }

            object_state state = object_state.capture(original);
            if (payload == payload_copy)
            {
                run_copy(state, build_command, sender_player_id);
                return;
            }

            float original_price = original.getRealPrice();
            foreach (dependent_build dependent in dependents)
            {
                EntranceExit previous = GameController.Instance.getSerializedObject<EntranceExit>(dependent.previous_id);
                if (previous != null)
                {
                    dependent.original_price = previous.getRealPrice();
                    dependent.state = object_state.capture(previous);
                }
            }

            set_refundable(original, false);
            if (!original.destruct())
            {
                set_refundable(original, true);
                return;
            }

            bool is_own_command = get_own_player_id() == sender_player_id;
            BuildableObject built_object;
            execution current = new execution(state.variant);
            current_execution = current;
            try
            {
                built_object = run_build(build_command, current, original_price, is_own_command);
                if (built_object is FlatRide flat_ride)
                {
                    foreach (dependent_build dependent in dependents)
                    {
                        FlatRideEntranceExitBuildCommand command = new FlatRideEntranceExitBuildCommand(flat_ride, dependent.reference_name, dependent.position, dependent.forward);
                        BuildableObject built_dependent = run_build(command, current, dependent.original_price, is_own_command);
                        if (built_dependent != null)
                        {
                            dependent.state?.apply_to(built_dependent);
                        }
                    }
                }
            }
            finally
            {
                current_execution = null;
            }

            if (built_object == null)
            {
                Debug.LogError("[move_it] Removed object #" + object_id + " but the rebuild produced nothing");
                return;
            }
            // After the dependents, because a ride can only reopen once its entrance and exit exist again.
            state.apply_to(built_object);
        }

        // The original stays, so there is no price to discount and the copy pays in full.
        private static void run_copy(object_state state, AbstractBaseBuildCommand build_command, byte sender_player_id)
        {
            BuildableObject built_object;
            execution current = new execution(state.variant);
            current_execution = current;
            try
            {
                built_object = run_build(build_command, current, 0f, get_own_player_id() == sender_player_id);
            }
            finally
            {
                current_execution = null;
            }

            if (built_object != null)
            {
                state.apply_settings_to(built_object);
            }
        }

        private static BuildableObject run_build(AbstractBaseBuildCommand command, execution current, float original_price, bool is_own_command)
        {
            current.original_price = original_price;
            current.built_object = null;
            // onAfterBuild shows the full price to everyone but the owner, which would be wrong for a move, so
            // the command poses as owned while it runs. The camera shake reads the flag later, after the animation.
            command.isOwnCommand = true;
            // Stops a rebuilt ride from opening its info window, which owned commands do unless they are replays.
            command.isReplayCommand = true;
            command.run();
            command.isOwnCommand = is_own_command;
            return current.built_object;
        }

        // Each object refunds itself, and destruct also goes through a ride's platforms, entrance and exit.
        private static void set_refundable(BuildableObject original, bool refundable)
        {
            List<SerializedMonoBehaviour> belonging = new List<SerializedMonoBehaviour>();
            original.retrieveObjectsBelongingToThis(belonging);
            foreach (SerializedMonoBehaviour item in belonging)
            {
                if (item is BuildableObject part)
                {
                    part.canBeRefunded = refundable;
                }
            }
        }

        private static byte get_own_player_id()
        {
            MultiplayerController multiplayer = CommandController.Instance.multiplayerController;
            return multiplayer != null ? multiplayer.ownState.playerID : (byte)0;
        }

        private static void write_build_command(BinaryWriter writer, move_session session, AbstractBaseBuildCommand build_command)
        {
            object data = AccessTools.Field(build_command.GetType(), "data").GetValue(build_command);
            // Before the switch: these commands hold the same data as a plain block, plus the ride.
            if (session.original is EntranceExit entrance_exit)
            {
                BlockBuildCommand.Data piece = (BlockBuildCommand.Data)data;
                writer.Write(kind_entrance_exit);
                writer.Write(piece.referenceName);
                write_colors(writer, null);
                write_vector(writer, piece.position);
                write_vector(writer, piece.forward);
                writer.Write(entrance_exit.attraction.objectID);
                return;
            }
            switch (data)
            {
                case DecoBuildCommand.Data deco:
                    writer.Write(kind_deco);
                    writer.Write(deco.referenceName);
                    write_colors(writer, deco.colors);
                    writer.Write(deco.size);
                    write_vector(writer, deco.position);
                    write_vector(writer, deco.eulerAngles);
                    write_vector(writer, deco.ghostVelocity);
                    break;
                case PathAttachmentBuildCommand.Data path_attachment:
                    writer.Write(kind_path_attachment);
                    writer.Write(path_attachment.referenceName);
                    write_colors(writer, path_attachment.colors);
                    writer.Write(path_attachment.pathID);
                    writer.Write(path_attachment.side);
                    break;
                case BlockBuildCommand.Data block:
                    writer.Write(kind_block);
                    writer.Write(block.referenceName);
                    write_colors(writer, null);
                    write_vector(writer, block.position);
                    write_vector(writer, block.forward);
                    break;
                case FlatRideBuildCommand.Data flat_ride:
                    writer.Write(kind_flat_ride);
                    writer.Write(flat_ride.referenceName);
                    write_colors(writer, null);
                    write_vector(writer, flat_ride.position);
                    write_vector(writer, flat_ride.forward);
                    write_dependents(writer, session, flat_ride.position, flat_ride.forward);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected build command " + build_command);
            }
        }

        private static void write_dependents(BinaryWriter writer, move_session session, Vector3 ride_position, Vector3 ride_forward)
        {
            Quaternion ride_rotation = Quaternion.LookRotation(ride_forward);
            writer.Write(session.dependents.Count);
            foreach (move_session.dependent dependent in session.dependents)
            {
                Vector3 forward = ride_rotation * dependent.local_rotation * Vector3.forward;
                writer.Write(dependent.original.objectID);
                writer.Write(dependent.reference_name);
                write_vector(writer, ride_position + ride_rotation * dependent.local_position);
                // Rides turn in steps of 90 degrees, so this only removes the rounding left by the rotations.
                write_vector(writer, new Vector3(Mathf.Round(forward.x), 0f, Mathf.Round(forward.z)));
            }
        }

        private static void read_dependents(BinaryReader reader, List<dependent_build> dependents)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                dependents.Add(new dependent_build
                {
                    previous_id = reader.ReadUInt32(),
                    reference_name = reader.ReadString(),
                    position = read_vector(reader),
                    forward = read_vector(reader)
                });
            }
        }

        // Returns null if the command can no longer be built, which must be known before the original is removed.
        private static AbstractBaseBuildCommand read_build_command(BinaryReader reader, List<dependent_build> dependents)
        {
            byte kind = reader.ReadByte();
            string reference_name = reader.ReadString();
            Color[] colors = read_colors(reader);
            switch (kind)
            {
                case kind_deco:
                    float size = reader.ReadSingle();
                    Vector3 position = read_vector(reader);
                    Vector3 euler_angles = read_vector(reader);
                    Vector3 ghost_velocity = read_vector(reader);
                    return new DecoBuildCommand(reference_name, position, euler_angles, ghost_velocity, size, colors);
                case kind_path_attachment:
                    uint path_id = reader.ReadUInt32();
                    int side = reader.ReadInt32();
                    Path path = GameController.Instance.getSerializedObject<Path>(path_id);
                    return path != null ? new PathAttachmentBuildCommand(reference_name, path, side, colors) : null;
                case kind_block:
                    Vector3 block_position = read_vector(reader);
                    Vector3 block_forward = read_vector(reader);
                    return new BlockBuildCommand(reference_name, block_position, block_forward);
                case kind_flat_ride:
                    Vector3 ride_position = read_vector(reader);
                    Vector3 ride_forward = read_vector(reader);
                    read_dependents(reader, dependents);
                    return new FlatRideBuildCommand(reference_name, ride_position, ride_forward);
                case kind_entrance_exit:
                    Vector3 piece_position = read_vector(reader);
                    Vector3 piece_forward = read_vector(reader);
                    Attraction attraction = GameController.Instance.getSerializedObject<Attraction>(reader.ReadUInt32());
                    return create_entrance_exit_command(attraction, reference_name, piece_position, piece_forward);
                default:
                    throw new InvalidOperationException("Unexpected build command kind " + kind);
            }
        }

        private static AbstractBaseBuildCommand create_entrance_exit_command(Attraction attraction, string reference_name, Vector3 position, Vector3 forward)
        {
            switch (attraction)
            {
                case FlatRide flat_ride:
                    return new FlatRideEntranceExitBuildCommand(flat_ride, reference_name, position, forward);
                case TrackedRide tracked_ride:
                    // The native build throws without this platform, and by then the original would already be gone.
                    Block block = GameController.Instance.park.blockData.getBlock(position - forward);
                    bool has_platform = block is StationPlatform platform && platform.trackSegment.track.TrackedRide == tracked_ride;
                    return has_platform ? new TrackedRideEntranceExitBuildCommand(tracked_ride, reference_name, position, forward) : null;
                case BoatRide boat_ride:
                    return new BoatRideEntranceExitBuildCommand(boat_ride, reference_name, position, forward);
                default:
                    return null;
            }
        }

        private static void write_colors(BinaryWriter writer, Color[] colors)
        {
            writer.Write(colors != null ? colors.Length : -1);
            if (colors != null)
            {
                foreach (Color color in colors)
                {
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);
                }
            }
        }

        private static Color[] read_colors(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0)
            {
                return null;
            }
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return colors;
        }

        private static void write_vector(BinaryWriter writer, Vector3 vector)
        {
            writer.Write(vector.x);
            writer.Write(vector.y);
            writer.Write(vector.z);
        }

        private static Vector3 read_vector(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
