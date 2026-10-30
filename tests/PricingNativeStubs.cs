// Minimal native surfaces for exercising the production pricing adapter without Unity or a save.
using System;
using System.Collections.Generic;

internal sealed class GameInventory { }
internal sealed class GameItem
{
    public GameInventory parentInventory;
    public long unitValue, lateUnitValue, ComplexValue, NativeQuote;
    public int WaterValue, NativeMisc, QuoteReads;
    public bool Owned = true, EffectivelyOwned = true;
    public List<ItemFeature> itemFeatures = new List<ItemFeature>();
    public HashSet<string> Tags = new HashSet<string>();
    public List<GameItem> Descendants = new List<GameItem>();
    public long GetComplexeItemValue() => ComplexValue;
    public long GetNegociatedValue() { QuoteReads++; return NativeQuote; }
    public int GetItemMiscValue(bool useMarkup = false) => NativeMisc;
    public List<GameItem> FindAllChildItems() => new List<GameItem>(Descendants);
    public bool IsTag(string tag) => Tags.Contains(tag);
    public bool IsGameItemType(string type) => Tags.Contains(type);
    public TagState GetTagReadonly(string tag) => new TagState { Value = ContrabandLevel };
    public string ContrabandLevel;
}
internal sealed class TagState { public string Value; public string GetString() => Value; }
internal sealed class StoreClient
{
    public bool Buying = true, isRequireVariety;
    public int perfumeInfluence;
    public string clientFaction;
    public List<string> clientItemFeatureBuying = new List<string>();
    public bool IsClientBuying() => Buying;
}
internal sealed class EmporiumEntry
{
    public static EmporiumEntry Instance;
    public GameInventory backInvinvElement = new GameInventory();
}
internal sealed class PlayerStore
{
    public static PlayerStore Instance;
    public int retailMarkup = 15;
    public bool AcceptMarkup = true;
    public HealthData healthData = new HealthData();
    public bool IsCurrentClientAcceptMarkup() => AcceptMarkup;
}
internal sealed class StoreStation
{
    public static StoreStation Instance;
    public StoreEventManager storeEventManager = new StoreEventManager();
}
internal sealed class StoreEventManager
{
    public List<StoreEvent> Events = new List<StoreEvent>();
    public List<StoreEvent> GetActiveEvents() => new List<StoreEvent>(Events);
}
internal sealed class StoreEvent { public List<NegociationData> negociationDatas = new List<NegociationData>(); }
internal sealed class NegociationData { public string identifier, itemType; public int value; }
internal static class GeneralHelper
{
    public static bool IsItemOwned(GameItem item) => item.Owned;
    public static bool IsItemEffectivelyOwned(GameItem item) => item.EffectivelyOwned;
}
internal static class WaterHelper { public static int GetContainerPrice(GameItem item) => item.WaterValue; }
internal sealed class HealthData
{
    public static bool Addicted;
    public int Penalty;
    public static bool HasAnyAddiction() => Addicted;
    public int GetTotalAddictionPenalty() => Penalty;
}
internal static class StartingPerk { public static bool Awful; public static bool IsAwfulNegotiator() => Awful; }
internal static class StoreReputation { public static bool Tips; public static bool IsPerkUnlocked(string id) => Tips; }
internal sealed class ItemFeature
{
    public enum FeatureType { Normale, Event, Special, Temporary, TemporaryBuying, TemporarySelling, ClientTemporary }
    public enum ValueStage { Innate, Market, Final }
    public string identifier, Display = "visible condition";
    public FeatureType featureType;
    public ValueStage Stage;
    public bool isDisabled, Hidden, Exposed;
    public int Current, Actual = 999;
    public ValueStage GetValueStage() => Stage;
    public int GetCurrentValueMod() => Exposed ? Actual : Current;
    public bool IsExposed() => Exposed;
    public bool IsInitiallyShown() => true;
    public bool GetPublicHidden() => Hidden;
    public string GetBaseCurrentDisplay() => Display;
    public static ItemFeature CreateFeatureFromNegociationData(NegociationData data)
        => new ItemFeature { identifier = data.identifier, Current = data.value, Stage = ValueStage.Market, featureType = FeatureType.Event };
}
internal static class ItemFeatureList
{
    public static Dictionary<string, Func<ItemFeature>> SpecialFeature = new Dictionary<string, Func<ItemFeature>>
    {
        ["discountedBuy20"] = () => Make("discountedBuy20", -20, ItemFeature.FeatureType.ClientTemporary)
    };
    public static ItemFeature Make(string id, int percent, ItemFeature.FeatureType type = ItemFeature.FeatureType.TemporarySelling)
        => new ItemFeature { identifier = id, Current = percent, Stage = ItemFeature.ValueStage.Market, featureType = type };
    public static ItemFeature RetailMarkUp() => Make("retailMarkUp", PlayerStore.Instance.retailMarkup);
    public static ItemFeature VarietyBuyerBonus() => Make("varietyBuyerBonus", 15, ItemFeature.FeatureType.ClientTemporary);
    public static ItemFeature TouristTax() => Make("touristTax", 15, ItemFeature.FeatureType.ClientTemporary);
    public static ItemFeature NeuroActivePerfumeSelling() => Make("neuroActivePerfumeSelling", 15);
    public static ItemFeature PheromonePerfumeSelling() => Make("pheromonePerfumeSelling", 25);
    public static ItemFeature AddictionSellPenalty() => Make("addictionSellPenalty", -PlayerStore.Instance.healthData.Penalty);
    public static ItemFeature AwfulNegotiatorSellPenalty() => Make("awfulNegotiatorSellPenalty", -5);
    public static ItemFeature Pourboire() => Make("tips", 15);
    public static ItemFeature RestrictedLowMarkUp() => Make("restrictedLowMarkUp", 17, ItemFeature.FeatureType.Special);
    public static ItemFeature RestrictedMidMarkUp() => Make("restrictedMidMarkUp", 35, ItemFeature.FeatureType.Special);
    public static ItemFeature RestrictedHighMarkUp() => Make("restrictedHighMarkUp", 50, ItemFeature.FeatureType.Special);
    public static ItemFeature ContrabandLowMarkUp() => Make("contrabandLowMarkUp", 35, ItemFeature.FeatureType.Special);
    public static ItemFeature ContrabandMidMarkUp() => Make("contrabandMidMarkUp", 70, ItemFeature.FeatureType.Special);
    public static ItemFeature ContrabandHighMarkUp() => Make("contrabandHighMarkUp", 100, ItemFeature.FeatureType.Special);
    public static ItemFeature ContrabandCriticalMarkUp() => Make("contrabandCriticalMarkUp", 150, ItemFeature.FeatureType.Special);
}
