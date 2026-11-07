using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace move_it
{
    // What a shop keeps across a move. BlockBuildCommand only carries the prefab, the position and the direction.
    // Must be captured before the original is destroyed, because ProductShop.onKill closes the shop.
    internal sealed class shop_state
    {
        private static readonly AccessTools.FieldRef<Shop, ShopTransactionHistories> transaction_histories_field =
            AccessTools.FieldRefAccess<Shop, ShopTransactionHistories>("transactionHistories");
        private static readonly AccessTools.FieldRef<Shop, float> satisfaction_rate_field =
            AccessTools.FieldRefAccess<Shop, float>("satisfactionRate");
        private static readonly AccessTools.FieldRef<Shop, string> customized_name_field =
            AccessTools.FieldRefAccess<Shop, string>("customizedName");
        private static readonly AccessTools.FieldRef<Shop, double> total_expenses_field =
            AccessTools.FieldRefAccess<Shop, double>("totalExpenses");
        private static readonly AccessTools.FieldRef<Shop, double> total_revenue_field =
            AccessTools.FieldRefAccess<Shop, double>("totalRevenue");
        private static readonly AccessTools.FieldRef<Shop, BuildingMaintenancePaymentProcessor> maintenance_processor_field =
            AccessTools.FieldRefAccess<Shop, BuildingMaintenancePaymentProcessor>("maintenancePaymentProcessor");
        internal static readonly AccessTools.FieldRef<PaymentProcessor, float> accumulated_debt_field =
            AccessTools.FieldRefAccess<PaymentProcessor, float>("accumulatedDebt");
        internal static readonly AccessTools.FieldRef<PaymentProcessor, float> payment_delta_time_field =
            AccessTools.FieldRefAccess<PaymentProcessor, float>("deltaTime");
        private static readonly AccessTools.FieldRef<ProductShop, int> out_of_resources_since_field =
            AccessTools.FieldRefAccess<ProductShop, int>("outOfResourcesSince");

        private static readonly MethodInfo customers_count_setter = AccessTools.PropertySetter(typeof(Shop), nameof(Shop.customersCount));
        private static readonly MethodInfo missed_last_month_setter = AccessTools.PropertySetter(typeof(Shop), nameof(Shop.missedCustomersLastMonthCount));
        private static readonly MethodInfo missed_current_month_setter = AccessTools.PropertySetter(typeof(Shop), nameof(Shop.missedCustomersCurrentMonthCount));
        private static readonly MethodInfo dirtiness_setter = AccessTools.PropertySetter(typeof(Shop), nameof(Shop.dirtiness));
        private static readonly MethodInfo last_cleaned_time_setter = AccessTools.PropertySetter(typeof(Shop), nameof(Shop.lastCleanedTime));
        private static readonly MethodInfo vouchers_redeemed_setter = AccessTools.PropertySetter(typeof(ProductShop), nameof(ProductShop.vouchersRedeemed));
        private static readonly MethodInfo money_withdrawn_setter = AccessTools.PropertySetter(typeof(CashMachine), nameof(CashMachine.moneyWithdrawn));
        private static readonly MethodInfo product_satisfaction_setter = AccessTools.PropertySetter(typeof(ProductSettings), nameof(ProductSettings.satisfactionRate));
        private static readonly MethodInfo product_sales_setter = AccessTools.PropertySetter(typeof(ProductSettings), nameof(ProductSettings.salesNumbers));
        private static readonly MethodInfo product_profits_setter = AccessTools.PropertySetter(typeof(ProductSettings), nameof(ProductSettings.profits));

        // Both only attach their visible products in Initialize, when the new stock is still empty.
        private static readonly MethodInfo balloon_shop_update_attached =
            AccessTools.Method(typeof(BalloonShop), "updateBalloonCountBasedOnStockContent");
        private static readonly MethodInfo attached_shop_update_attached =
            AccessTools.Method(typeof(ProductShopWithProductsAttached), "updateBalloonCountBasedOnStockContent");

        private sealed class product_state
        {
            public string reference_name;
            public float price;
            public bool synchronized_price;
            public bool selected;
            public Color[] colors;
            public float satisfaction_rate;
            public int sales_numbers;
            public double profits;
        }

        private string customized_name;
        private bool opened;
        private float satisfaction_rate;
        private float dirtiness;
        private int last_cleaned_time;
        private float cleaning_threshold;
        private bool synchronized_cleaning_threshold;
        private int customers_count;
        private int missed_last_month;
        private int missed_current_month;
        private ShopTransactionHistories transaction_histories;
        private double total_expenses;
        private double total_revenue;
        private float maintenance_debt;
        private float maintenance_delta_time;
        private Color[] colors;

        private Dictionary<string, float> stock;
        private Depot depot;
        private bool serves_poisoned_food;
        private int vouchers_redeemed;
        private int out_of_resources_since;
        private string mirrored_shop;
        private List<product_state> products;

        private float admission_price;
        private bool admission_synchronized_price;

        private int money_withdrawn;

        public static shop_state capture(Shop source)
        {
            shop_state state = new shop_state();
            state.customized_name = customized_name_field(source);
            state.opened = source.opened;
            state.satisfaction_rate = satisfaction_rate_field(source);
            state.dirtiness = source.dirtiness;
            state.last_cleaned_time = source.lastCleanedTime;
            state.cleaning_threshold = source.cleaningThreshold;
            state.synchronized_cleaning_threshold = source.synchronizedCleaningThreshold;
            state.customers_count = source.customersCount;
            state.missed_last_month = source.missedCustomersLastMonthCount;
            state.missed_current_month = source.missedCustomersCurrentMonthCount;
            state.transaction_histories = transaction_histories_field(source);
            state.total_expenses = total_expenses_field(source);
            state.total_revenue = total_revenue_field(source);

            BuildingMaintenancePaymentProcessor maintenance_processor = maintenance_processor_field(source);
            if (maintenance_processor != null)
            {
                state.maintenance_debt = accumulated_debt_field(maintenance_processor);
                state.maintenance_delta_time = payment_delta_time_field(maintenance_processor);
            }

            CustomColors custom_colors = source.GetComponent<CustomColors>();
            if (custom_colors != null)
            {
                state.colors = custom_colors.getColors();
            }

            if (source is ProductShop product_shop)
            {
                state.capture_product_shop(product_shop);
            }
            if (source is AdmissionShop admission_shop)
            {
                AdmissionShopSettings settings = (AdmissionShopSettings)admission_shop.getSettings();
                state.admission_price = settings.price;
                state.admission_synchronized_price = settings.synchronizedPrice;
            }
            if (source is CashMachine cash_machine)
            {
                state.money_withdrawn = cash_machine.moneyWithdrawn;
            }
            return state;
        }

        private void capture_product_shop(ProductShop source)
        {
            stock = new Dictionary<string, float>(source.stock.resourceNameContentAssoc);
            depot = source.depot;
            serves_poisoned_food = source.servesPoisonedFood;
            vouchers_redeemed = source.vouchersRedeemed;
            out_of_resources_since = out_of_resources_since_field(source);
            mirrored_shop = (source as CustomizableProductShop)?.getMirroredShopReferenceName();

            products = new List<product_state>();
            foreach (KeyValuePair<string, ProductSettings> entry in ((ProductShopSettings)source.getSettings()).productNameSettingsAssoc)
            {
                ProductSettings settings = entry.Value;
                products.Add(new product_state
                {
                    reference_name = entry.Key,
                    price = settings.price,
                    synchronized_price = settings.synchronizedPrice,
                    selected = settings.selected,
                    colors = settings.colors,
                    satisfaction_rate = settings.satisfactionRate,
                    sales_numbers = settings.salesNumbers,
                    profits = settings.profits
                });
            }
        }

        public void apply_to(Shop target)
        {
            // Initialize may have named the new shop after its zone. A null name is valid: it means the default one.
            target.setCustomizedName(customized_name);
            satisfaction_rate_field(target) = satisfaction_rate;
            target.cleaningThreshold = cleaning_threshold;
            target.synchronizedCleaningThreshold = synchronized_cleaning_threshold;
            customers_count_setter.Invoke(target, new object[] { customers_count });
            missed_last_month_setter.Invoke(target, new object[] { missed_last_month });
            missed_current_month_setter.Invoke(target, new object[] { missed_current_month });
            transaction_histories_field(target) = transaction_histories;
            total_expenses_field(target) = total_expenses;
            total_revenue_field(target) = total_revenue;

            BuildingMaintenancePaymentProcessor maintenance_processor = maintenance_processor_field(target);
            if (maintenance_processor != null)
            {
                accumulated_debt_field(maintenance_processor) = maintenance_debt;
                payment_delta_time_field(maintenance_processor) = maintenance_delta_time;
            }

            if (colors != null)
            {
                target.GetComponent<CustomColors>()?.setColors(colors);
            }

            // Closed first, so that a closed shop doesn't order ingredients or queue a cleaning job below.
            if (!opened)
            {
                target.close();
            }

            if (target is ProductShop product_shop)
            {
                apply_to_product_shop(product_shop);
            }
            if (target is AdmissionShop admission_shop)
            {
                AdmissionShopSettings settings = (AdmissionShopSettings)admission_shop.getSettings();
                settings.price = admission_price;
                settings.synchronizedPrice = admission_synchronized_price;
            }
            if (target is CashMachine cash_machine)
            {
                money_withdrawn_setter.Invoke(cash_machine, new object[] { money_withdrawn });
            }

            // After the mirrored shop: whether a customizable shop can get dirty depends on it.
            dirtiness_setter.Invoke(target, new object[] { dirtiness });
            last_cleaned_time_setter.Invoke(target, new object[] { last_cleaned_time });
        }

        private void apply_to_product_shop(ProductShop target)
        {
            // Stock and depot go first: changing the selected products makes the shop order what it's missing.
            foreach (KeyValuePair<string, float> entry in stock)
            {
                target.stock.modify(entry.Key, entry.Value);
            }
            if (depot != null)
            {
                target.setDepot(depot);
            }

            if (mirrored_shop != null && target is CustomizableProductShop customizable_shop)
            {
                customizable_shop.setMirroredShop(ScriptableSingleton<AssetManager>.Instance.getPrefab<ProductShop>(mirrored_shop));
            }

            ProductShopSettings settings = (ProductShopSettings)target.getSettings();
            foreach (product_state product in products)
            {
                if (!settings.productNameSettingsAssoc.TryGetValue(product.reference_name, out ProductSettings product_settings))
                {
                    continue;
                }
                product_settings.setPriceSync(product.synchronized_price);
                product_settings.setPrice(product.price);
                product_settings.setColors(product.colors);
                product_settings.setSelected(product.selected);
                product_satisfaction_setter.Invoke(product_settings, new object[] { product.satisfaction_rate });
                product_sales_setter.Invoke(product_settings, new object[] { product.sales_numbers });
                product_profits_setter.Invoke(product_settings, new object[] { product.profits });
            }

            target.servesPoisonedFood = serves_poisoned_food;
            vouchers_redeemed_setter.Invoke(target, new object[] { vouchers_redeemed });
            out_of_resources_since_field(target) = out_of_resources_since;

            if (target is BalloonShop)
            {
                balloon_shop_update_attached.Invoke(target, null);
            }
            else if (target is ProductShopWithProductsAttached)
            {
                attached_shop_update_attached.Invoke(target, null);
            }
        }
    }
}
