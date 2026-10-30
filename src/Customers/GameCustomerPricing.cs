using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ProbablyAI.Customers
{
    internal static class GameCustomerPricing
    {
        public static CustomerPrice Read(GameItem item, StoreClient client)
        {
            // Only a top-level counter item has had the native weighted-area callbacks applied.
            bool onCounter = ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.backInvinvElement);
            bool project = !onCounter && GeneralHelper.IsItemOwned(item) && client.IsClientBuying();
            var price = Build(item, client, project, root: true, inheritedMarket: 0);
            price.Source = onCounter ? "counter_offer" : project ? "projected_counter_offer" : "current_value";
            if (project)
            {
                long contents = 0;
                if (!item.IsTag("IGNORE_CHILD_VALUE"))
                {
                    // The native formula flattens descendants; do not count a nested bag's contents twice.
                    long inherited = CustomerPriceCalculator.InheritedMarketPercent(price);
                    foreach (var child in item.FindAllChildItems())
                        if (!child.IsTag("ITEM_BEHIND_LOCK_TAG"))
                            contents += CustomerPriceCalculator.Calculate(Build(child, client, true, root: false, inheritedMarket: inherited));
                }
                price.ContentsValue = (int)contents;
                price.MiscValue = (int)item.lateUnitValue + price.ContentsValue;
                price.Value = CustomerPriceCalculator.Calculate(price);
            }
            else
            {
                price.MiscValue = item.GetItemMiscValue(useMarkup: true);
                price.ContentsValue = price.MiscValue - (int)item.lateUnitValue;
                CustomerPriceCalculator.Calculate(price); // Populate the explanation only.
                price.Value = item.GetNegociatedValue(); // The game's actual quote is authoritative.
            }
            return price;
        }

        private static CustomerPrice Build(GameItem item, StoreClient client, bool project, bool root, long inheritedMarket)
        {
            var price = new CustomerPrice
            {
                BaseValue = item.unitValue + item.GetComplexeItemValue(),
                MarketScaledValue = WaterHelper.GetContainerPrice(item), MiscValue = (int)item.lateUnitValue
            };
            var ids = new HashSet<string>();
            foreach (var feature in item.itemFeatures)
            {
                if (project && feature.featureType == ItemFeature.FeatureType.Event) continue;
                ids.Add(feature.identifier); // Disabled features also block native AddItemFeature duplicates.
                Add(price, feature, false);
            }
            if (!project) return price;

            void Missing(ItemFeature feature)
            {
                if (ids.Add(feature.identifier)) Add(price, feature, true);
            }

            // Read current events, rather than relying on event features attached only after a move.
            foreach (var activeEvent in StoreStation.Instance.storeEventManager.GetActiveEvents())
                foreach (var data in activeEvent.negociationDatas)
                    if (root ? item.IsGameItemType(data.itemType) : item.IsTag(data.itemType))
                        Missing(ItemFeature.CreateFeatureFromNegociationData(data));

            if ((root || GeneralHelper.IsItemEffectivelyOwned(item)) && PlayerStore.Instance.IsCurrentClientAcceptMarkup())
                Missing(ItemFeatureList.RetailMarkUp());

            if (root)
            {
                if (client.perfumeInfluence == 1) Missing(ItemFeatureList.NeuroActivePerfumeSelling());
                else if (client.perfumeInfluence == 2) Missing(ItemFeatureList.PheromonePerfumeSelling());
                if (HealthData.HasAnyAddiction() && PlayerStore.Instance.healthData.GetTotalAddictionPenalty() != 0)
                    Missing(ItemFeatureList.AddictionSellPenalty());
                if (StartingPerk.IsAwfulNegotiator()) Missing(ItemFeatureList.AwfulNegotiatorSellPenalty());
                if (client.clientFaction == "FACTION_UPPER_LEVEL" && StoreReputation.IsPerkUnlocked("UL_TIPS"))
                    Missing(ItemFeatureList.Pourboire());
                if (client.isRequireVariety) Missing(ItemFeatureList.VarietyBuyerBonus());
                if (client.clientFaction == "FACTION_TOURIST") Missing(ItemFeatureList.TouristTax());
                foreach (var id in client.clientItemFeatureBuying ?? new List<string>())
                    if (ItemFeatureList.SpecialFeature.TryGetValue(id, out var create)) Missing(create());
                if (item.IsGameItemType("RESTRICTED_1")) Missing(ItemFeatureList.RestrictedLowMarkUp());
                else if (item.IsGameItemType("RESTRICTED_2")) Missing(ItemFeatureList.RestrictedMidMarkUp());
                else if (item.IsGameItemType("RESTRICTED_3")) Missing(ItemFeatureList.RestrictedHighMarkUp());
            }
            if (item.IsTag("CONTRABAND_ITEM_TAG"))
            {
                switch (item.GetTagReadonly("CONTRABAND_LEVEL").GetString())
                {
                    case "CONTRABAND_LEVEL_LOW": Missing(ItemFeatureList.ContrabandLowMarkUp()); break;
                    case "CONTRABAND_LEVEL_MID": Missing(ItemFeatureList.ContrabandMidMarkUp()); break;
                    case "CONTRABAND_LEVEL_HIGH": Missing(ItemFeatureList.ContrabandHighMarkUp()); break;
                    case "CONTRABAND_LEVEL_CRITICAL": Missing(ItemFeatureList.ContrabandCriticalMarkUp()); break;
                }
            }
            if (inheritedMarket != 0)
                price.Modifiers.Add(new PriceModifier { Id = "container_client_market", Stage = "market", Percent = inheritedMarket, Projected = true });
            return price;
        }

        private static void Add(CustomerPrice price, ItemFeature feature, bool projected)
        {
            if (feature.isDisabled) return;
            var stage = feature.GetValueStage();
            bool visible = feature.IsInitiallyShown() && (!feature.GetPublicHidden() || feature.IsExposed());
            price.Modifiers.Add(new PriceModifier
            {
                Id = visible ? feature.identifier : null,
                // Use the current public/exposed value, never the hidden real condition.
                Name = visible ? Regex.Replace(feature.GetBaseCurrentDisplay() ?? "", "<[^>]*>", "") : null,
                Stage = stage == ItemFeature.ValueStage.Market ? "market" : stage == ItemFeature.ValueStage.Final ? "final" : "innate",
                Percent = feature.GetCurrentValueMod(), Projected = projected,
                InheritsToContents = stage == ItemFeature.ValueStage.Market && feature.identifier != "retailMarkUp" &&
                    (feature.featureType == ItemFeature.FeatureType.Temporary || feature.featureType == ItemFeature.FeatureType.TemporaryBuying ||
                     feature.featureType == ItemFeature.FeatureType.TemporarySelling || feature.featureType == ItemFeature.FeatureType.ClientTemporary)
            });
        }
    }
}
