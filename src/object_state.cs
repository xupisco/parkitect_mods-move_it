using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace move_it
{
    // What the native build commands don't carry over from the original object.
    internal sealed class object_state
    {
        private static readonly MethodInfo spawned_at_time_setter =
            AccessTools.PropertySetter(typeof(SerializedMonoBehaviour), nameof(SerializedMonoBehaviour.spawnedAtTime));
        private static readonly MethodInfo times_emptied_setter =
            AccessTools.PropertySetter(typeof(TrashBin), nameof(TrashBin.timesEmptied));
        private static readonly MethodInfo total_collected_volume_setter =
            AccessTools.PropertySetter(typeof(TrashBin), nameof(TrashBin.totalCollectedVolume));

        private static readonly AccessTools.FieldRef<SerializedMonoBehaviour, List<SerializedRawObject>> custom_data_list =
            AccessTools.FieldRefAccess<SerializedMonoBehaviour, List<SerializedRawObject>>("customDataList");
        private static readonly AccessTools.FieldRef<BuildableObject, bool> placed_in_scenario_editor =
            AccessTools.FieldRefAccess<BuildableObject, bool>("hasBeenPlacedInScenarioEditor");
        private static readonly AccessTools.FieldRef<VariantsDeco, int> variant_field =
            AccessTools.FieldRefAccess<VariantsDeco, int>("variant");

        private int spawned_at_time;
        private bool was_placed_in_scenario_editor;
        private List<SerializedRawObject> custom_data;
        private float override_scenery_rating_score;
        private bool effect_default_enabled;
        private bool has_been_vandalized;
        private float trash_contents;
        private int trash_times_emptied;
        private float trash_total_collected_volume;
        private shop_state shop;
        private attraction_state attraction;

        public int variant { get; private set; } = -1;

        public static int get_variant(BuildableObject buildable_object)
        {
            return buildable_object is VariantsDeco variants_deco ? variant_field(variants_deco) : -1;
        }

        public static void set_variant(VariantsDeco variants_deco, int variant)
        {
            variant_field(variants_deco) = variant;
        }

        public static object_state capture(BuildableObject source)
        {
            object_state state = new object_state();
            state.spawned_at_time = source.spawnedAtTime;
            state.was_placed_in_scenario_editor = placed_in_scenario_editor(source);
            state.custom_data = custom_data_list(source);
            state.variant = get_variant(source);

            if (source is EffectDecoObject.IEffectDecoObjectProperties effect_properties)
            {
                state.effect_default_enabled = effect_properties.getEffectDefaultEnabled();
            }
            if (source is Deco deco)
            {
                state.override_scenery_rating_score = deco.overrideSceneryRatingScore;
            }
            if (source is PathAttachment path_attachment)
            {
                state.has_been_vandalized = path_attachment.hasBeenVandalized;
            }
            if (source is TrashBin trash_bin)
            {
                state.trash_contents = trash_bin.Contents;
                state.trash_times_emptied = trash_bin.timesEmptied;
                state.trash_total_collected_volume = trash_bin.totalCollectedVolume;
            }
            if (source is Shop source_shop)
            {
                state.shop = shop_state.capture(source_shop);
            }
            if (source is Attraction source_attraction)
            {
                state.attraction = attraction_state.capture(source_attraction);
            }
            return state;
        }

        // For a copy of a deco, which is a new object: its age and custom data are its own.
        public void apply_settings_to(BuildableObject target)
        {
            if (target is EffectDecoObject.IEffectDecoObjectProperties effect_properties)
            {
                effect_properties.setEffectDefaultEnabled(effect_default_enabled);
            }
            if (target is Deco deco)
            {
                deco.overrideSceneryRatingScore = override_scenery_rating_score;
            }
        }

        public void apply_to(BuildableObject target)
        {
            // Keeps the resale depreciation: otherwise moving and selling would refund the full price.
            spawned_at_time_setter.Invoke(target, new object[] { spawned_at_time });
            placed_in_scenario_editor(target) = was_placed_in_scenario_editor;
            custom_data_list(target) = custom_data;

            if (target is EffectDecoObject.IEffectDecoObjectProperties effect_properties)
            {
                effect_properties.setEffectDefaultEnabled(effect_default_enabled);
            }
            if (target is Deco deco)
            {
                deco.overrideSceneryRatingScore = override_scenery_rating_score;
            }
            if (target is TrashBin trash_bin)
            {
                trash_bin.Contents = trash_contents;
                times_emptied_setter.Invoke(trash_bin, new object[] { trash_times_emptied });
                total_collected_volume_setter.Invoke(trash_bin, new object[] { trash_total_collected_volume });
            }
            // Otherwise moving would repair the object for free, while PathAttachment.repair charges its price.
            if (has_been_vandalized && target is PathAttachment path_attachment)
            {
                path_attachment.vandalize();
            }
            if (shop != null && target is Shop target_shop)
            {
                shop.apply_to(target_shop);
            }
            if (attraction != null && target is Attraction target_attraction)
            {
                attraction.apply_to(target_attraction);
            }
        }
    }
}
