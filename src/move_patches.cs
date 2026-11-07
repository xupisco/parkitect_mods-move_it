using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace move_it
{
    // Every patch is inert unless a move is in progress: either the local preview (move_session)
    // or the synchronized execution (move_command.current_execution).

    // The preview's price tag and its money check must see the move's cost, not the full price.
    [HarmonyPatch(typeof(Builder), "calculateTotalBuildingCosts")]
    internal static class builder_total_costs_patch
    {
        [HarmonyPostfix]
        private static void postfix(Builder __instance, ref float __result)
        {
            move_session session = move_session.current;
            if (session != null && session.builder == __instance && !session.is_copying)
            {
                __result = move_command.get_move_cost(__result, session.get_original_price());
            }
        }
    }

    // BlockBuilder pushes the ghost out of any occupied tile, which would stop a shop from being turned where it stands.
    [HarmonyPatch(typeof(BlockBuilder), "makeValidBuildPosition")]
    internal static class block_builder_valid_position_patch
    {
        private static readonly MethodInfo get_block = AccessTools.Method(typeof(BlockData), nameof(BlockData.getBlock), new[] { typeof(Vector3) });
        private static readonly MethodInfo get_block_ignoring_original_method = AccessTools.Method(typeof(block_builder_valid_position_patch), nameof(get_block_ignoring_original));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!replaced && instruction.Calls(get_block))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = get_block_ignoring_original_method;
                    replaced = true;
                }
                yield return instruction;
            }
            // Refuses to enable after a game update changes this method, instead of misplacing shops.
            if (!replaced)
            {
                throw new System.InvalidOperationException("BlockBuilder.makeValidBuildPosition no longer calls BlockData.getBlock");
            }
        }

        private static Block get_block_ignoring_original(BlockData block_data, Vector3 position)
        {
            Block block = block_data.getBlock(position);
            move_session session = move_session.current;
            // A copy can't stand where the original still does.
            return session != null && !session.is_copying && session.is_part_of_original(block) ? null : block;
        }
    }

    // The build position is rounded to the deco's height steps, which would undo a height fine-tuned with another mod.
    [HarmonyPatch(typeof(DecoBuilder), "makeValidBuildPosition")]
    internal static class deco_builder_valid_position_patch
    {
        [HarmonyPostfix]
        private static void postfix(DecoBuilder __instance, Vector3 position, ref Vector3 __result)
        {
            move_session session = move_session.current;
            if (session != null && session.builder == __instance && session.keeps_fine_height(position.y))
            {
                __result.y = position.y;
            }
        }
    }

    // BlueprintBuilder only reads from a file, and a blueprint file is a PNG that would show up in the player's list.
    // During a group move it reads the data in memory instead, the way BlueprintBuilderImplementation does.
    [HarmonyPatch(typeof(BlueprintBuilder), "instantiateBuildableObjectPreview")]
    internal static class blueprint_builder_preview_patch
    {
        private static readonly ConstructorInfo file_constructor =
            AccessTools.Constructor(typeof(SavegameDeserializer), new[] { typeof(string), typeof(SerializationContext) });
        private static readonly MethodInfo open_deserializer_method =
            AccessTools.Method(typeof(blueprint_builder_preview_patch), nameof(open_deserializer));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!replaced && instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, file_constructor))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = open_deserializer_method;
                    replaced = true;
                }
                yield return instruction;
            }
            // Refuses to enable after a game update changes this method.
            if (!replaced)
            {
                throw new System.InvalidOperationException("BlueprintBuilder.instantiateBuildableObjectPreview no longer opens a file");
            }
        }

        private static SavegameDeserializer open_deserializer(string file_path, SerializationContext context)
        {
            move_session session = move_session.current;
            if (session == null || !session.is_group)
            {
                return new SavegameDeserializer(file_path, context);
            }
            MemoryStream stream = new MemoryStream(session.blueprint_data);
            return new SavegameDeserializer(SavegameDeserializer.getBlueprintStreamReader(stream), stream, context);
        }
    }

    // Adds a ride's entrance and exit to its ghost, the way PathAttachmentBuilder shows several attachments in one ghost.
    // As children they turn with the ride, and in actualBuiltObjects the builder validates and prices them too.
    [HarmonyPatch(typeof(Builder), "instantiateBuildableObjectPreview")]
    internal static class builder_preview_dependents_patch
    {
        private static readonly AccessTools.FieldRef<Builder, GameObject> ghost_field =
            AccessTools.FieldRefAccess<Builder, GameObject>("ghost");
        private static readonly AccessTools.FieldRef<Builder, List<BuildableObject>> actual_built_objects =
            AccessTools.FieldRefAccess<Builder, List<BuildableObject>>("actualBuiltObjects");

        [HarmonyPostfix]
        private static void postfix(Builder __instance)
        {
            move_session session = move_session.current;
            if (session == null || session.builder != __instance || session.is_copying)
            {
                return;
            }

            GameObject ghost = ghost_field(__instance);
            List<BuildableObject> built_objects = actual_built_objects(__instance);
            if (session.tower_height >= 0f && built_objects.Count > 0 && built_objects[0] is TowerRide tower_ghost)
            {
                attraction_state.apply_tower_height_to_ghost(tower_ghost, session.tower_height);
            }

            foreach (move_session.dependent item in session.dependents)
            {
                EntranceExit copy = Object.Instantiate(ScriptableSingleton<AssetManager>.Instance.getPrefab<EntranceExit>(item.reference_name));
                copy.dontRegisterBlock = true;
                copy.transform.SetParent(ghost.transform);
                copy.transform.localPosition = item.local_position;
                copy.transform.localRotation = item.local_rotation;
                copy.gameObject.SetActive(true);
                copy.canBeRefunded = false;
                copy.dontSerialize = true;
                copy.isPreview = true;
                copy.Initialize();
                // Block.render snaps to the tile grid based on where the ghost happens to be right now.
                copy.transform.localPosition = item.local_position;
                built_objects.Add(copy);
            }
        }
    }

    // Replaces the native build command with the atomic move command. The native commands are generated anyway
    // so the mod doesn't have to duplicate how each builder works out position, rotation, size and colors.
    [HarmonyPatch]
    internal static class builder_generate_commands_patch
    {
        private static readonly AccessTools.FieldRef<Builder, Vector3> ghost_position =
            AccessTools.FieldRefAccess<Builder, Vector3>("ghostPos");
        private static readonly AccessTools.FieldRef<Builder, Vector3> builder_forward =
            AccessTools.FieldRefAccess<Builder, Vector3>("forward");
        private static readonly MethodInfo blueprint_valid_position =
            AccessTools.Method(typeof(BlueprintBuilder), "makeValidBuildPosition");

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> target_methods()
        {
            yield return AccessTools.Method(typeof(DecoBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(PathAttachmentBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(BlockBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(FlatRideBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(FlatRideEntranceExitBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(TrackedRideEntranceExitBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(BoatRideEntranceExitBuilder), "generateBuildCommands");
            yield return AccessTools.Method(typeof(BlueprintBuilder), "generateBuildCommands");
        }

        [HarmonyPostfix]
        private static void postfix(Builder __instance, ref IEnumerable<AbstractBaseBuildCommand> __result)
        {
            move_session session = move_session.current;
            if (session == null || session.builder != __instance)
            {
                return;
            }

            IEnumerable<AbstractBaseBuildCommand> native_commands = __result;
            // Set first: if anything below throws, the native build must not go through at full price.
            __result = Enumerable.Empty<AbstractBaseBuildCommand>();

            // The native commands aren't generated for a group, because BlueprintBuilder would try to open its file.
            if (session.is_group)
            {
                Vector3 position = (Vector3)blueprint_valid_position.Invoke(__instance, new object[] { ghost_position(__instance) });
                move_command.send_group(session, position, builder_forward(__instance));
                return;
            }

            AbstractBaseBuildCommand build_command = native_commands.FirstOrDefault(command => command != null);
            if (build_command != null)
            {
                bool copy = session.is_copying;
                if (copy)
                {
                    session.mark_copy_sent();
                }
                move_command.send(session, build_command, copy);
            }
        }
    }

    // An entrance or exit must stay at its station. At another one, the native build would destroy the piece already
    // there and leave this station without one, and the tracked ride builder doesn't even check the platform's ride.
    // Hiding the other platforms from the ghost makes the builders' own "next to a station" check do the refusing.
    // The check can't be patched directly: patching canBuildAt crashes the game's Mono inside MonoMod while loading a
    // park. The likely cause is its CanBuild return value, so avoid patching methods that return that struct.
    [HarmonyPatch(typeof(Block), nameof(Block.findAdjacent), new[] { typeof(Vector3), typeof(BlockData), typeof(bool) })]
    internal static class block_find_adjacent_patch
    {
        [HarmonyPostfix]
        private static void postfix(Block __instance, List<BlockNeighbour> __result)
        {
            // In this order because this runs for every block: isPreview is the cheap check that rules out nearly all.
            if (!__instance.isPreview)
            {
                return;
            }
            move_session session = move_session.current;
            if (session != null && session.station != null)
            {
                __result.RemoveAll(neighbour => neighbour.block is AttractionPlatform platform && platform.getStationController() != session.station);
            }
        }
    }

    // The original is still standing during the preview, but it will be gone by the time the new one is built.
    // A copy is the exception: the original stays, so it can be in the way.
    [HarmonyPatch(typeof(BuildableObject), nameof(BuildableObject.getCollidingObjects))]
    internal static class buildable_colliding_objects_patch
    {
        [HarmonyPostfix]
        private static void postfix(BuildableObject __instance, List<GameObject> __result)
        {
            if (!__instance.isPreview)
            {
                return;
            }
            move_session session = move_session.current;
            if (session != null && !session.is_copying)
            {
                __result.RemoveAll(colliding => colliding != null && session.is_part_of_original(colliding.GetComponentInParent<BuildableObject>()));
            }
        }
    }

    // Holding the height key places one attachment on every side of the path, which makes no sense for a move.
    [HarmonyPatch(typeof(PathAttachmentBuilder), "instantiateBuildableObjectPreview")]
    internal static class attachment_builder_preview_patch
    {
        private static readonly AccessTools.FieldRef<PathAttachmentBuilder, bool> multi_build =
            AccessTools.FieldRefAccess<PathAttachmentBuilder, bool>("multiBuild");

        [HarmonyPrefix]
        private static void prefix(PathAttachmentBuilder __instance)
        {
            if (move_session.is_builder(__instance))
            {
                multi_build(__instance) = false;
            }
        }
    }

    // Building on an occupied side destroys what is there. A move is allowed to, but a copy only adds to the park.
    [HarmonyPatch(typeof(Path), nameof(Path.getAttachmentPoints))]
    internal static class path_attachment_points_patch
    {
        [HarmonyPostfix]
        private static void postfix(Path __instance, List<Path.PathAttachmentPoint> __result)
        {
            move_session session = move_session.current;
            if (session != null && session.is_copying)
            {
                __result.RemoveAll(point => __instance.hasAttachmentOnSide(point.side));
            }
        }
    }

    // Trees and rocks get a random rotation when the ghost is created, after the session has set the original's.
    [HarmonyPatch(typeof(DecoBuilder), "setRandomRotation")]
    internal static class deco_builder_random_rotation_patch
    {
        [HarmonyPrefix]
        private static bool prefix(DecoBuilder __instance)
        {
            return !move_session.is_builder(__instance);
        }
    }

    // VariantsDeco draws a random variant in Initialize, which would change how the object looks. The draw itself still
    // happens, and advances the synchronized generator in the same way on every client.
    [HarmonyPatch(typeof(VariantsDeco), "instantiateVariant")]
    internal static class variants_deco_instantiate_patch
    {
        [HarmonyPrefix]
        private static void prefix(VariantsDeco __instance, ref int variantNr)
        {
            move_command.execution execution = move_command.current_execution;
            if (execution != null)
            {
                if (!__instance.isPreview && execution.variant >= 0)
                {
                    variantNr = execution.variant;
                    object_state.set_variant(__instance, execution.variant);
                }
                return;
            }

            // Keeps the ghost matching the object. The name check leaves other previews alone, like the deco window's.
            move_session session = move_session.current;
            if (session != null && __instance.isPreview && session.variant >= 0 && __instance.getReferenceName() == session.reference_name)
            {
                variantNr = session.variant;
            }
        }
    }

    // Charges only the move's cost and exposes the new instance, which BuilderImplementation.execute doesn't return.
    [HarmonyPatch]
    internal static class builder_implementation_costs_patch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> target_methods()
        {
            yield return AccessTools.Method(typeof(BuilderImplementation<DecoBuildCommand.Data>), "calculateBuildingCosts");
            yield return AccessTools.Method(typeof(BuilderImplementation<PathAttachmentBuildCommand.Data>), "calculateBuildingCosts");
            yield return AccessTools.Method(typeof(BuilderImplementation<BlockBuildCommand.Data>), "calculateBuildingCosts");
            yield return AccessTools.Method(typeof(BuilderImplementation<FlatRideBuildCommand.Data>), "calculateBuildingCosts");
            yield return AccessTools.Method(typeof(BuilderImplementation<BlueprintBuildCommand.Data>), "calculateBuildingCosts");
        }

        [HarmonyPostfix]
        private static void postfix(BuildableObject builtObject, ref float __result)
        {
            move_command.execution execution = move_command.current_execution;
            if (execution != null)
            {
                __result = move_command.get_move_cost(__result, execution.on_built(builtObject));
            }
        }
    }
}
