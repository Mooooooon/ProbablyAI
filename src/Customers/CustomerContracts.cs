using System.Collections.Generic;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Customers
{
    public sealed class CustomerSnapshot
    {
        // absent, arriving, or present. Only an arrived visitor has readable details.
        public string Status { get; set; }
        public CustomerInfo Customer { get; set; }
    }

    public sealed class CustomerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Identifier { get; set; }
        public string Faction { get; set; }
        public string Intent { get; set; }
        public string IntentDescription { get; set; }
        public bool BusinessComplete { get; set; }
        public CustomerBudget PurchaseBudget { get; set; }
        public CustomerPreferences Preferences { get; set; }
        public CustomerWanted Wanted { get; set; }
        public CustomerAug Aug { get; set; }
        public DialogueInfo CurrentDialogue { get; set; }
        public List<DialogueLine> DialogueHistory { get; set; }
        public BarterNeeds Barter { get; set; }
        public ProcurementNeeds Procurement { get; set; }
        public GunOrderNeeds GunOrder { get; set; }
        public int ServiceCost { get; set; }
    }

    public sealed class CustomerWanted
    {
        public bool IsWanted { get; set; }
        public bool Stealth { get; set; }
        public string QuietReminder { get; set; }

        // Suppress the hint after a report/capture, and never expose the real name or bounty.
        public static CustomerWanted Create(bool isWanted, bool stealth, bool reported, string posterId,
            IDictionary<string, int> wantedStatus)
        {
            if (!isWanted || reported) return null;
            if (wantedStatus != null && !string.IsNullOrEmpty(posterId) &&
                wantedStatus.TryGetValue(posterId, out var state) && state == 1) return null;
            return new CustomerWanted
            {
                IsWanted = true, Stealth = stealth,
                QuietReminder = T("（小声）主人，这个人符合通缉令上的特征呢。")
            };
        }
    }

    public sealed class CustomerAug
    {
        public bool IsAug { get; set; }
        public string QuietReminder { get; set; }

        // The native flag is enough for a quiet hint; never expose tier or hidden scan state.
        public static CustomerAug Create(bool isAug)
        {
            if (!isAug) return null;
            return new CustomerAug
            {
                IsAug = true, QuietReminder = T("（小声）主人，这个人好像是改造体呢。")
            };
        }
    }

    public sealed class CustomerBudget
    {
        public bool Enabled { get; set; }
        public int? Remaining { get; set; }
        public string Note { get; set; }
    }

    public sealed class CustomerPreferences
    {
        public List<string> BuyingTags { get; set; } = new List<string>();
        public List<string> BuyingIdentifiers { get; set; } = new List<string>();
        public List<string> RejectedTags { get; set; } = new List<string>();
        public List<string> RejectedIdentifiers { get; set; } = new List<string>();
        public bool HasCustomPurchaseRule { get; set; }
        public bool RequiresVariety { get; set; }
        public List<string> PreviouslyBoughtIdentifiers { get; set; } = new List<string>();
        public bool MultiplePurchaseDisabled { get; set; }
        public bool RefusesContraband { get; set; }
        public bool RefusesHazardousWaterLabel { get; set; }
        public int AcceptedChemicalProductGrade { get; set; }
        public bool CustomerSellingDisabled { get; set; }
        public string Note { get; set; }
    }

    public sealed class DialogueInfo
    {
        public string Speaker { get; set; }
        public string Text { get; set; }
    }

    public sealed class DialogueLine
    {
        public string Speaker { get; set; }
        public string Text { get; set; }
    }

    public sealed class BarterNeeds
    {
        public string Description { get; set; }
        public bool IsFreeReward { get; set; }
        public List<BarterRequirement> Requirements { get; set; } = new List<BarterRequirement>();
    }

    public sealed class BarterRequirement
    {
        public int Index { get; set; }
        public string RequiredIdentifier { get; set; }
        public string RequiredTag { get; set; }
        public string RequiredDisplay { get; set; }
        public string OfferedDisplay { get; set; }
        public List<string> RejectedConditions { get; set; } = new List<string>();
    }

    public sealed class ProcurementNeeds
    {
        public string Description { get; set; }
        public int InitialReward { get; set; }
        public int FinalReward { get; set; }
    }

    public sealed class GunOrderNeeds
    {
        public string Description { get; set; }
        public bool IsServiceOrder { get; set; }
        public int BaseReward { get; set; }
        public List<GunRequirement> Mandatory { get; set; } = new List<GunRequirement>();
        public List<GunRequirement> Bonus { get; set; } = new List<GunRequirement>();
    }

    public sealed class GunRequirement
    {
        public string Description { get; set; }
        public string Tag { get; set; }
        public int Value { get; set; }
        public string Comparison { get; set; }
        public int BaseReward { get; set; }
    }

    public sealed class CustomerItemCheck
    {
        public string CustomerId { get; set; }
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public string ContainerId { get; set; }
        public bool BusinessComplete { get; set; }
        public bool MatchesRegularPurchaseRules { get; set; }
        public bool? WithinRemainingBudget { get; set; }
        public CustomerBudget PurchaseBudget { get; set; }
        public long CurrentItemValue { get; set; }
        public bool ValueIsEstimate { get; set; } = true;
        public CustomerPrice Pricing { get; set; }
        public List<int> MatchingBarterRequirementIndexes { get; set; } = new List<int>();
        public string Note { get; set; }
    }

    public sealed class CustomerPrice
    {
        // counter_offer is the native live quote; projected_counter_offer is a read-only preview.
        public string Source { get; set; }
        public long BaseValue { get; set; }
        public int MarketScaledValue { get; set; }
        public int MiscValue { get; set; }
        public int ContentsValue { get; set; }
        public long InnatePercent { get; set; }
        public long MarketPercent { get; set; }
        public double FinalMultiplier { get; set; }
        public List<PriceModifier> Modifiers { get; set; } = new List<PriceModifier>();
        internal long Value { get; set; }
    }

    public sealed class PriceModifier
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // innate and market add within their stage; each final modifier multiplies independently.
        public string Stage { get; set; }
        public long Percent { get; set; }
        public bool Projected { get; set; }
        public bool InheritsToContents { get; set; }
    }

    public interface ICustomerBackend
    {
        CustomerSnapshot Capture();
        CustomerItemCheck CheckItem(string expectedCustomerId, string itemId);
    }
}
