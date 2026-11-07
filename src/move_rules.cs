using System.Collections.Generic;
using HarmonyLib;

namespace move_it
{
    // Decides what is allowed to move.
    internal static class move_rules
    {
        private static readonly AccessTools.FieldRef<EffectDecoObject, List<EffectBoxHandle>> effect_box_handles =
            AccessTools.FieldRefAccess<EffectDecoObject, List<EffectBoxHandle>>("effectBoxHandles");
        private static readonly AccessTools.FieldRef<StationController, List<StationController>> synchronized_stations =
            AccessTools.FieldRefAccess<StationController, List<StationController>>("synchronizedStations");

        public static bool is_candidate(BuildableObject buildable_object)
        {
            return buildable_object is Deco
                || buildable_object is PathAttachment
                || buildable_object is Shop
                || buildable_object is FlatRide
                || buildable_object is EntranceExit;
        }

        // What can be copied when picked on its own. A ride's entrance or exit can't, and neither can a group.
        public static bool can_copy(BuildableObject buildable_object)
        {
            return buildable_object is Deco
                || buildable_object is PathAttachment
                || buildable_object is Shop
                || buildable_object is FlatRide;
        }

        // Paths are never moved. Picking one only opens the game's path builder with its settings, as the native picker does.
        public static bool is_copy_only(BuildableObject buildable_object)
        {
            return buildable_object is Path;
        }

        // Returns null when the path's settings can be copied. The native picker doesn't offer these paths at all.
        public static string get_copy_refusal_reason(BuildableObject buildable_object)
        {
            return buildable_object.isAvailable() ? null : texts.path_not_available;
        }

        // Returns null when the object can be moved.
        public static string get_refusal_reason(BuildableObject buildable_object)
        {
            if (!is_candidate(buildable_object) || has_unsupported_state(buildable_object))
            {
                return texts.type_not_supported;
            }
            if (!buildable_object.canBeDestroyedByPlayer())
            {
                return texts.cannot_be_changed;
            }
            if (is_linked_to_effect_box(buildable_object))
            {
                return texts.linked_to_effect_box;
            }
            if (has_running_campaign(buildable_object))
            {
                return texts.running_campaign;
            }
            if (buildable_object is FlatRide flat_ride)
            {
                return get_flat_ride_refusal_reason(flat_ride);
            }
            if (buildable_object is EntranceExit entrance_exit)
            {
                return get_entrance_exit_refusal_reason(entrance_exit);
            }
            return null;
        }

        // Path attachments need the path below them, and entrances and exits are part of their ride, so neither can be
        // moved on their own as part of a group.
        public static string get_group_refusal_reason(BuildableObject buildable_object)
        {
            bool can_join_group = buildable_object is Deco || buildable_object is Shop || buildable_object is FlatRide;
            if (!can_join_group || !buildable_object.canBeBlueprinted)
            {
                return texts.not_in_group;
            }
            return get_refusal_reason(buildable_object);
        }

        private static string get_entrance_exit_refusal_reason(EntranceExit entrance_exit)
        {
            if (entrance_exit.attraction == null || entrance_exit.connectedAttractionPlatform == null)
            {
                return texts.not_connected;
            }
            // The same rule the game applies to removing one: EntranceExit.TryKill refuses while the ride isn't closed.
            if (entrance_exit.attraction.state != Attraction.State.CLOSED)
            {
                return texts.close_ride_first;
            }
            return null;
        }

        private static string get_flat_ride_refusal_reason(FlatRide flat_ride)
        {
            // A new ride would be repaired for free, and copying the condition would leave it without a repair job.
            if (flat_ride.isBroken())
            {
                return texts.ride_broken;
            }
            // Attraction.onKill undoes the link with the other ride.
            foreach (StationController station in flat_ride.getStationControllers())
            {
                List<StationController> synchronized = synchronized_stations(station);
                if (synchronized != null && synchronized.Count > 0)
                {
                    return texts.ride_synchronized;
                }
            }
            if (is_effect_box_trigger(flat_ride))
            {
                return texts.ride_triggers_effect_box;
            }
            return null;
        }

        // Destroying the advertised object cancels its campaign, and the money spent on it would be lost.
        private static bool has_running_campaign(BuildableObject buildable_object)
        {
            if (!(buildable_object is IAdvertisable advertisable))
            {
                return false;
            }
            foreach (AbstractCampaign campaign in GameController.Instance.park.campaignRunner.runningCampaigns)
            {
                if (campaign is AdvertisingCampaign advertising_campaign && advertising_campaign.isAdvertisementFor(advertisable))
                {
                    return true;
                }
            }
            return false;
        }

        // The park keeps no list of effect boxes, but this only runs for rides and only while one is hovered or moved.
        private static bool is_effect_box_trigger(FlatRide flat_ride)
        {
            foreach (SerializedMonoBehaviour serialized_object in GameController.Instance.getSerializedObjects())
            {
                if (serialized_object is EffectBox effect_box
                    && effect_box.trigger is AttractionTrigger
                    && effect_box.settings?.attractions != null
                    && effect_box.settings.attractions.Contains(flat_ride))
                {
                    return true;
                }
            }
            return false;
        }

        // These carry state or need a builder that the build commands used here can't reproduce.
        private static bool has_unsupported_state(BuildableObject buildable_object)
        {
            return buildable_object is LayeredText
                || buildable_object is ImageBanner
                || buildable_object is Speaker
                || buildable_object is EffectBox
                || buildable_object is EffectFireworksLauncher
                || buildable_object is RideCamera
                || buildable_object is SplashBattleTarget
                || buildable_object is PathTextSign
                || buildable_object is SupportSteelTowerDeco
                || buildable_object is WoodenSupportsDeco
                || buildable_object is ScenarioEditorOnlyDeco
                || buildable_object is SceneryRatingSource
                || buildable_object is CustomCampaigns.CustomScenarioMarker;
        }

        // Destroying a linked object clears its entry in the effect box timeline.
        private static bool is_linked_to_effect_box(BuildableObject buildable_object)
        {
            EffectDecoObject effect = buildable_object.GetComponent<EffectDecoObject>();
            if (effect == null)
            {
                return false;
            }
            List<EffectBoxHandle> handles = effect_box_handles(effect);
            return handles != null && handles.Count > 0;
        }
    }
}
