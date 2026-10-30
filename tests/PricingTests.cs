using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Hands;

internal static partial class Program
{
    private static async Task RunPricingTests()
    {
        await Check("price stages add internally and negotiation factors multiply independently", () =>
        {
            var price = new CustomerPrice { BaseValue = 100, Modifiers = {
                Mod("condition", "innate", 20), Mod("variety", "market", 15), Mod("retail", "market", 15),
                Mod("event", "market", 50), Mod("discount", "final", -20), Mod("bargain", "final", 10) } };
            Assert(CustomerPriceCalculator.Calculate(price) == 190, "stages collapsed into an additive or all-multiplicative formula");
            Assert(price.InnatePercent == 20 && price.MarketPercent == 80 && Math.Abs(price.FinalMultiplier - .88) < 1e-12, "stage explanation incorrect");
        });
        await Check("water scales with market while contents and late value precede final multipliers", () =>
        {
            var price = new CustomerPrice { BaseValue = 100, MarketScaledValue = 10, MiscValue = 20,
                Modifiers = { Mod("wear", "innate", -50), Mod("event", "market", 100), Mod("bargain", "final", -50) } };
            Assert(CustomerPriceCalculator.Calculate(price) == 70, "miscellaneous or water value used the wrong stage");
        });
        await Check("native midpoint rounding and nonnegative stage clamps are preserved", () =>
        {
            var price = new CustomerPrice { BaseValue = 5, Modifiers = { Mod("bargain", "final", -50) } };
            Assert(CustomerPriceCalculator.Calculate(price) == 2, "midpoint must round to even");
            price.BaseValue = 7; Assert(CustomerPriceCalculator.Calculate(price) == 4, "upper midpoint rounding");
            price.MiscValue = 10; price.Modifiers.Clear(); price.Modifiers.Add(Mod("crash", "market", -200));
            Assert(CustomerPriceCalculator.Calculate(price) == 10, "market clamp erased late value");
            price.Modifiers.Add(Mod("bargain", "final", -200));
            Assert(CustomerPriceCalculator.Calculate(price) == 0, "negative final factor not clamped");
        });
        await Check("inventory preview includes variety, live events and retail before testing budget", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true;
            var item = FoodPriceItem(); PriceEvent(50);
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 180 && price.Source == "projected_counter_offer", "pre-counter bonuses omitted");
            Assert(CustomerPriceCalculator.WithinBudget(price, new CustomerBudget { Enabled = true, Remaining = 170 }, true) == false, "base-price affordability survived markup");
            Assert(CustomerPriceCalculator.WithinBudget(price, new CustomerBudget { Enabled = true, Remaining = 180 }, true) == true, "exact budget boundary rejected");
        });
        await Check("existing variety and retail features are not projected twice", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; var item = FoodPriceItem(); PriceEvent(50);
            item.itemFeatures.Add(ItemFeatureList.VarietyBuyerBonus()); item.itemFeatures.Add(ItemFeatureList.RetailMarkUp());
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 180 && price.Modifiers.Count == 3, "existing bonuses counted twice");
        });
        await Check("native complex value, liquid value and late value enter the correct preview stages", () =>
        {
            var client = PricingWorld(); PlayerStore.Instance.retailMarkup = 100;
            var item = FoodPriceItem(); item.unitValue = 80; item.ComplexValue = 20; item.WaterValue = 10; item.lateUnitValue = 20;
            item.itemFeatures.Add(new ItemFeature { identifier = "wear", Current = -50 });
            item.itemFeatures.Add(new ItemFeature { identifier = "bargain", Stage = ItemFeature.ValueStage.Final, Current = -50 });
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 70 && price.BaseValue == 100 && price.MiscValue == 20 && price.MarketScaledValue == 10, "native inputs entered the wrong stage");
        });
        await Check("inventory events and retail fluctuations are refreshed on every query", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; var item = FoodPriceItem();
            item.itemFeatures.Add(ItemFeature.CreateFeatureFromNegociationData(new NegociationData { identifier = "old-event", value = 100 }));
            var data = PriceEvent(50);
            Assert(GameCustomerPricing.Read(item, client).Value == 180, "stale attached event double counted");
            data.value = -20; PlayerStore.Instance.retailMarkup = 30;
            Assert(GameCustomerPricing.Read(item, client).Value == 125, "price context was cached");
        });
        await Check("live top-level counter quote is authoritative and receives no projected bonuses", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; PriceEvent(50);
            var item = FoodPriceItem(); item.NativeQuote = 233; item.parentInventory = EmporiumEntry.Instance.backInvinvElement;
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 233 && price.Source == "counter_offer" && price.Modifiers.Count == 0 && item.QuoteReads == 1, "native quote was recomputed or marked as a preview");
        });
        await Check("disabled features stay disabled and native duplicate rules are respected", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; var item = FoodPriceItem();
            var retail = ItemFeatureList.RetailMarkUp(); retail.isDisabled = true;
            var variety = ItemFeatureList.VarietyBuyerBonus(); variety.isDisabled = true;
            item.itemFeatures.Add(retail); item.itemFeatures.Add(variety);
            Assert(GameCustomerPricing.Read(item, client).Value == 100, "disabled price feature was re-enabled");
        });
        await Check("customers refusing retail markup receive no projected retail bonus", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; PlayerStore.Instance.AcceptMarkup = false;
            Assert(GameCustomerPricing.Read(FoodPriceItem(), client).Value == 115, "retail refusal ignored");
        });
        await Check("nested contents inherit client market effects once and retail independently", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; var bag = FoodPriceItem();
            bag.Descendants.Add(new GameItem { unitValue = 10 });
            bag.Descendants.Add(new GameItem { unitValue = 20 });
            bag.Descendants.Add(new GameItem { unitValue = 5 }); // Native descendants are already flat.
            var price = GameCustomerPricing.Read(bag, client);
            Assert(price.Value == 175 && price.ContentsValue == 45, "nested content, client inheritance or retail was duplicated");
        });
        await Check("locked and ignored contents follow native valuation exclusions", () =>
        {
            var client = PricingWorld(); var bag = FoodPriceItem();
            var hidden = new GameItem { unitValue = 1000 }; hidden.Tags.Add("ITEM_BEHIND_LOCK_TAG"); bag.Descendants.Add(hidden);
            Assert(GameCustomerPricing.Read(bag, client).Value == 115, "locked contents included");
            hidden.Tags.Clear(); bag.Tags.Add("IGNORE_CHILD_VALUE");
            Assert(GameCustomerPricing.Read(bag, client).Value == 115, "ignored contents included");
        });
        await Check("customer feature inheritance excludes event, contraband and final negotiation", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true;
            client.clientItemFeatureBuying.Add("discountedBuy20");
            var bag = FoodPriceItem(); bag.Descendants.Add(new GameItem { unitValue = 100 });
            var quote = GameCustomerPricing.Read(bag, client);
            Assert(quote.Value == 220 && quote.ContentsValue == 110, "client buying discount not inherited");
            var market = Mod("event", "market", 50); market.InheritsToContents = false; quote.Modifiers.Add(market);
            quote.Modifiers.Add(Mod("bargain", "final", 50));
            Assert(CustomerPriceCalculator.InheritedMarketPercent(quote) == -5, "non-inheritable stage was inherited");
        });
        await Check("known perfume, tourist and player penalties use their native market stage", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; client.clientFaction = "FACTION_TOURIST"; client.perfumeInfluence = 2;
            HealthData.Addicted = true; PlayerStore.Instance.healthData.Penalty = 10; StartingPerk.Awful = true;
            Assert(GameCustomerPricing.Read(FoodPriceItem(), client).Value == 155, "known contextual effects omitted");
        });
        await Check("current public conditions are used without revealing hidden condition names", () =>
        {
            var client = PricingWorld(); PlayerStore.Instance.AcceptMarkup = false; var item = FoodPriceItem();
            item.itemFeatures.Add(new ItemFeature { identifier = "hidden-real-id", Current = 20, Actual = 999, Hidden = true, Display = "hidden-real-name" });
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 120 && !HandsProtocol.Json(price).Contains("hidden-real-"), "hidden actual condition, ID or label leaked");
        });
        await Check("exposure refreshes condition value and counter miscellaneous value remains native", () =>
        {
            var client = PricingWorld(); PlayerStore.Instance.AcceptMarkup = false; var item = FoodPriceItem();
            var feature = new ItemFeature { identifier = "condition", Current = 20, Actual = -20 }; item.itemFeatures.Add(feature);
            Assert(GameCustomerPricing.Read(item, client).Value == 120, "public condition missing");
            feature.Exposed = true; Assert(GameCustomerPricing.Read(item, client).Value == 80, "exposed condition stayed cached");
            item.parentInventory = EmporiumEntry.Instance.backInvinvElement; item.NativeQuote = 88; item.NativeMisc = 8;
            var price = GameCustomerPricing.Read(item, client);
            Assert(price.Value == 88 && price.MiscValue == 8, "counter content value was projected");
        });
        await Check("contraband and restricted goods preview the native surcharge without mutating tags", () =>
        {
            var client = PricingWorld(); var item = FoodPriceItem(); item.Tags.Add("CONTRABAND_ITEM_TAG"); item.ContrabandLevel = "CONTRABAND_LEVEL_LOW";
            Assert(GameCustomerPricing.Read(item, client).Value == 150, "contraband surcharge omitted");
            item.Tags.Remove("CONTRABAND_ITEM_TAG"); item.Tags.Add("RESTRICTED_1");
            Assert(GameCustomerPricing.Read(item, client).Value == 132 && item.itemFeatures.Count == 0, "restricted surcharge omitted or added to live item");
        });
        await Check("pricing previews leave the live item tree, features and ownership unchanged", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true; var item = FoodPriceItem(); PriceEvent(50);
            var inventory = new GameInventory(); item.parentInventory = inventory;
            var original = new ItemFeature { identifier = "condition", Current = 20 }; item.itemFeatures.Add(original);
            var child = new GameItem { unitValue = 10 }; item.Descendants.Add(child);
            var quote = GameCustomerPricing.Read(item, client);
            Assert(quote.Value > 100 && item.QuoteReads == 0 && item.parentInventory == inventory && item.Owned &&
                item.itemFeatures.Count == 1 && ReferenceEquals(item.itemFeatures[0], original) && original.Current == 20 &&
                item.Descendants.Single() == child && child.itemFeatures.Count == 0, "preview changed game data");
        });
        await Check("non-sale items and unknown budgets never claim customer affordability", () =>
        {
            var client = PricingWorld(); var item = FoodPriceItem(); item.Owned = false; item.NativeQuote = 80;
            var quote = GameCustomerPricing.Read(item, client);
            Assert(quote.Value == 80 && quote.Source == "current_value", "customer-owned item received retail sale preview");
            var budget = new CustomerBudget { Enabled = true, Remaining = 100 };
            Assert(CustomerPriceCalculator.WithinBudget(quote, budget, false) == null, "purchase from customer compared to customer budget");
            budget.Enabled = false; Assert(CustomerPriceCalculator.WithinBudget(quote, budget, true) == null, "disabled budget guessed");
            budget.Enabled = true; budget.Remaining = null;
            Assert(CustomerPriceCalculator.WithinBudget(quote, budget, true) == null, "unknown budget guessed");
            item.Owned = true; client.Buying = false;
            Assert(GameCustomerPricing.Read(item, client).Source == "current_value", "non-buying customer received a sale preview");
        });
        await Check("tool price explanation serializes stages without exposing internal calculation state", () =>
        {
            var client = PricingWorld(); client.isRequireVariety = true;
            var price = GameCustomerPricing.Read(FoodPriceItem(), client);
            var data = JObject.Parse(HandsProtocol.Json(new CustomerItemCheck { CurrentItemValue = price.Value, Pricing = price }));
            Assert((long)data["current_item_value"] == 130 && (string)data["pricing"]["source"] == "projected_counter_offer" &&
                (long)data["pricing"]["market_percent"] == 30 && data["pricing"]["value"] == null, "model price contract incorrect");
        });
    }

    private static PriceModifier Mod(string id, string stage, int percent) => new PriceModifier { Id = id, Stage = stage, Percent = percent };
    private static StoreClient PricingWorld()
    {
        EmporiumEntry.Instance = new EmporiumEntry(); PlayerStore.Instance = new PlayerStore(); StoreStation.Instance = new StoreStation();
        HealthData.Addicted = false; StartingPerk.Awful = false; StoreReputation.Tips = false;
        return new StoreClient();
    }
    private static GameItem FoodPriceItem()
    {
        var item = new GameItem { unitValue = 100 }; item.Tags.Add("FOOD"); return item;
    }
    private static NegociationData PriceEvent(int percent)
    {
        var data = new NegociationData { identifier = "food-event", itemType = "FOOD", value = percent };
        StoreStation.Instance.storeEventManager.Events.Add(new StoreEvent { negociationDatas = { data } }); return data;
    }
}
