using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace move_it
{
    // What a flat ride keeps across a move. FlatRideBuildCommand only carries the prefab, the position and the direction.
    // Must be captured before the original is destroyed, because Attraction.onKill closes the ride.
    internal sealed class attraction_state
    {
        private static readonly AccessTools.FieldRef<Attraction, float> satisfaction_rate_field =
            AccessTools.FieldRefAccess<Attraction, float>("satisfactionRate");
        private static readonly AccessTools.FieldRef<Attraction, float> safety_field =
            AccessTools.FieldRefAccess<Attraction, float>("safety");
        private static readonly AccessTools.FieldRef<Attraction, int> next_breakdown_time_field =
            AccessTools.FieldRefAccess<Attraction, int>("nextBreakdownTime");
        private static readonly AccessTools.FieldRef<Attraction, string> customized_name_field =
            AccessTools.FieldRefAccess<Attraction, string>("customizedName");
        private static readonly AccessTools.FieldRef<Attraction, AttractionTransactionHistories> transaction_histories_field =
            AccessTools.FieldRefAccess<Attraction, AttractionTransactionHistories>("transactionHistories");
        private static readonly AccessTools.FieldRef<Attraction, double> total_expenses_field =
            AccessTools.FieldRefAccess<Attraction, double>("totalExpenses");
        private static readonly AccessTools.FieldRef<Attraction, double> total_revenue_field =
            AccessTools.FieldRefAccess<Attraction, double>("totalRevenue");
        private static readonly AccessTools.FieldRef<Attraction, AttractionMaintenancePaymentProcessor> maintenance_processor_field =
            AccessTools.FieldRefAccess<Attraction, AttractionMaintenancePaymentProcessor>("maintenancePaymentProcessor");
        private static readonly AccessTools.FieldRef<TowerRide, float> tower_height_field =
            AccessTools.FieldRefAccess<TowerRide, float>("height");
        private static readonly AccessTools.FieldRef<TowerRide, bool> tower_height_changed_field =
            AccessTools.FieldRefAccess<TowerRide, bool>("heightHasBeenChanged");

        private static readonly MethodInfo customers_count_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.customersCount));
        private static readonly MethodInfo customers_last_month_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.customersLastMonthCount));
        private static readonly MethodInfo customers_current_month_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.customersCurrentMonthCount));
        private static readonly MethodInfo missed_last_month_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.missedCustomersLastMonthCount));
        private static readonly MethodInfo missed_current_month_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.missedCustomersCurrentMonthCount));
        private static readonly MethodInfo breakdowns_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.breakdowns));
        private static readonly MethodInfo last_inspection_time_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.lastInspectionTime));
        private static readonly MethodInfo next_break_reason_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.nextBreakReason));
        private static readonly MethodInfo last_crash_time_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.lastCrashTime));
        private static readonly MethodInfo vouchers_redeemed_setter = AccessTools.PropertySetter(typeof(Attraction), nameof(Attraction.vouchersRedeemed));

        private string customized_name;
        private bool stats_are_outdated;
        private float entrance_fee;
        private int minimum_wait_time;
        private int maximum_wait_time;
        private Attraction.FillAmountWait fill_amount_wait;
        private float maintenance_budget;
        private int maintenance_interval;
        private string ride_music;
        private float ride_music_volume;
        private bool disable_fences;
        private float duration;
        private List<object> settings;
        private Color[] colors;

        private Attraction.State state;
        private float satisfaction_rate;
        private float safety;
        private int customers_count;
        private int customers_last_month;
        private int customers_current_month;
        private int missed_last_month;
        private int missed_current_month;
        private int breakdowns;
        private int last_inspection_time;
        private int next_breakdown_time;
        private Attraction.BreakReason next_break_reason;
        private float last_crash_time;
        private int vouchers_redeemed;
        private AttractionTransactionHistories transaction_histories;
        private double total_expenses;
        private double total_revenue;
        private float maintenance_debt;
        private float maintenance_delta_time;

        private bool disable_lights;
        private Color[] light_colors;

        private float tower_height = -1f;

        public static attraction_state capture(Attraction source)
        {
            attraction_state state = new attraction_state();
            state.customized_name = customized_name_field(source);
            state.stats_are_outdated = source.statsAreOutdated;
            state.entrance_fee = source.entranceFee;
            state.minimum_wait_time = source.minimumWaitTime;
            state.maximum_wait_time = source.maximumWaitTime;
            state.fill_amount_wait = source.fillAmountWait;
            state.maintenance_budget = source.maintenanceBudget;
            state.maintenance_interval = source.maintenanceInterval;
            state.ride_music = source.rideMusicReferenceName;
            state.ride_music_volume = source.rideMusicVolume;
            state.disable_fences = source.disableFences;
            state.duration = source.duration;
            state.settings = capture_settings(source);
            state.colors = source.GetComponent<CustomColors>()?.getColors();

            state.state = source.state;
            state.satisfaction_rate = satisfaction_rate_field(source);
            state.safety = safety_field(source);
            state.customers_count = source.customersCount;
            state.customers_last_month = source.customersLastMonthCount;
            state.customers_current_month = source.customersCurrentMonthCount;
            state.missed_last_month = source.missedCustomersLastMonthCount;
            state.missed_current_month = source.missedCustomersCurrentMonthCount;
            state.breakdowns = source.breakdowns;
            state.last_inspection_time = source.lastInspectionTime;
            state.next_breakdown_time = next_breakdown_time_field(source);
            state.next_break_reason = source.nextBreakReason;
            state.last_crash_time = source.lastCrashTime;
            state.vouchers_redeemed = source.vouchersRedeemed;
            state.transaction_histories = transaction_histories_field(source);
            state.total_expenses = total_expenses_field(source);
            state.total_revenue = total_revenue_field(source);

            AttractionMaintenancePaymentProcessor maintenance_processor = maintenance_processor_field(source);
            if (maintenance_processor != null)
            {
                state.maintenance_debt = shop_state.accumulated_debt_field(maintenance_processor);
                state.maintenance_delta_time = shop_state.payment_delta_time_field(maintenance_processor);
            }

            if (source is FlatRide flat_ride)
            {
                state.disable_lights = flat_ride.disableLights;
                state.light_colors = flat_ride.lightColors;
            }
            if (source is TowerRide tower_ride)
            {
                state.tower_height = tower_ride.getHeight();
            }
            return state;
        }

        public void apply_to(Attraction target)
        {
            // Initialize doesn't name rides, but a null name is valid here too: it means the default one.
            target.setCustomizedName(customized_name);
            target.statsAreOutdated = stats_are_outdated;
            target.setEntranceFee(entrance_fee);
            // Each of these raises the other when they would cross, so setting the maximum first can't distort the minimum.
            target.setMaximumWaitTime(maximum_wait_time);
            target.setMinimumWaitTime(minimum_wait_time);
            target.setFillAmountWait(fill_amount_wait);
            target.setMaintenanceBudget(maintenance_budget);
            target.setMaintenanceInterval(maintenance_interval);
            target.setRideMusic(ride_music);
            target.setRideMusicVolume(ride_music_volume);
            target.setDisableFences(disable_fences);
            apply_settings(target, settings);
            target.duration = duration;
            if (colors != null)
            {
                target.GetComponent<CustomColors>()?.setColors(colors);
            }

            satisfaction_rate_field(target) = satisfaction_rate;
            safety_field(target) = safety;
            customers_count_setter.Invoke(target, new object[] { customers_count });
            customers_last_month_setter.Invoke(target, new object[] { customers_last_month });
            customers_current_month_setter.Invoke(target, new object[] { customers_current_month });
            missed_last_month_setter.Invoke(target, new object[] { missed_last_month });
            missed_current_month_setter.Invoke(target, new object[] { missed_current_month });
            breakdowns_setter.Invoke(target, new object[] { breakdowns });
            last_crash_time_setter.Invoke(target, new object[] { last_crash_time });
            vouchers_redeemed_setter.Invoke(target, new object[] { vouchers_redeemed });
            transaction_histories_field(target) = transaction_histories;
            total_expenses_field(target) = total_expenses;
            total_revenue_field(target) = total_revenue;

            AttractionMaintenancePaymentProcessor maintenance_processor = maintenance_processor_field(target);
            if (maintenance_processor != null)
            {
                shop_state.accumulated_debt_field(maintenance_processor) = maintenance_debt;
                shop_state.payment_delta_time_field(maintenance_processor) = maintenance_delta_time;
            }

            if (target is FlatRide flat_ride)
            {
                flat_ride.setDisableLights(disable_lights);
                if (light_colors != null)
                {
                    flat_ride.setLightColors(light_colors);
                }
            }
            if (tower_height >= 0f && target is TowerRide tower_ride)
            {
                apply_tower_height(tower_ride, tower_height);
            }

            // Last, because the new entrance and exit must already exist when the ride opens.
            target.setState(state);
            // After the state, because opening counts as an inspection and restarts the breakdown timer.
            last_inspection_time_setter.Invoke(target, new object[] { last_inspection_time });
            next_breakdown_time_field(target) = next_breakdown_time;
            next_break_reason_setter.Invoke(target, new object[] { next_break_reason });
        }

        // setHeight charges for the difference from the current height. Setting the field first leaves no difference,
        // so it only rebuilds the sections and the bounds. The flag is what allows a tower to open.
        private static void apply_tower_height(TowerRide tower_ride, float height)
        {
            tower_height_field(tower_ride) = height;
            tower_height_changed_field(tower_ride) = true;
            tower_ride.setHeight(height);
        }

        // Gives the ghost its real height, so that the collision check covers the whole tower.
        public static void apply_tower_height_to_ghost(TowerRide ghost, float height)
        {
            tower_height_field(ghost) = height;
            ghost.spawnSections(height);
            ghost.updateSectionBounds();
        }

        private static List<object> capture_settings(Attraction source)
        {
            List<object> values = new List<object>();
            foreach (AttractionSetting setting in source.getAttractionSettings())
            {
                switch (setting)
                {
                    case AttractionSettingToggle toggle:
                        values.Add(toggle.value);
                        break;
                    case AttractionSettingDropdown dropdown:
                        values.Add(dropdown.value);
                        break;
                    case AttractionSettingNumberSlider number_slider:
                        values.Add(number_slider.value);
                        break;
                    case AttractionSettingDuration setting_duration:
                        values.Add(setting_duration.duration);
                        break;
                    default:
                        values.Add(null);
                        break;
                }
            }
            return values;
        }

        private static void apply_settings(Attraction target, List<object> values)
        {
            IList<AttractionSetting> target_settings = target.getAttractionSettings();
            for (int i = 0; i < values.Count && i < target_settings.Count; i++)
            {
                switch (target_settings[i])
                {
                    case AttractionSettingToggle toggle when values[i] is bool toggle_value:
                        toggle.setValue(toggle_value);
                        break;
                    case AttractionSettingDropdown dropdown when values[i] is int dropdown_value:
                        dropdown.setValue(dropdown_value);
                        break;
                    case AttractionSettingNumberSlider number_slider when values[i] is int number_value:
                        number_slider.setValue(number_value);
                        break;
                    case AttractionSettingDuration setting_duration when values[i] is int duration_value:
                        setting_duration.setValue(duration_value);
                        break;
                }
            }
        }
    }
}
