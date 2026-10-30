using System;
using System.Linq;

namespace ProbablyAI.Customers
{
    internal static class CustomerPriceCalculator
    {
        // GameItem.ComposeStagedValue: round once, after all three stages and miscellaneous value.
        public static long Calculate(CustomerPrice price)
        {
            price.InnatePercent = price.Modifiers.Where(m => m.Stage == "innate").Sum(m => m.Percent);
            price.MarketPercent = price.Modifiers.Where(m => m.Stage == "market").Sum(m => m.Percent);
            price.FinalMultiplier = 1.0;
            foreach (var modifier in price.Modifiers.Where(m => m.Stage == "final"))
                price.FinalMultiplier *= Math.Max(0.0, 1.0 + modifier.Percent / 100.0);
            double innate = Math.Max(0.0, price.BaseValue * (1.0 + price.InnatePercent / 100.0));
            double market = Math.Max(0.0, 1.0 + price.MarketPercent / 100.0);
            return Math.Max(0L, (long)Math.Round(((innate + price.MarketScaledValue) * market + price.MiscValue) * price.FinalMultiplier));
        }

        public static long InheritedMarketPercent(CustomerPrice price)
            => price.Modifiers.Where(m => m.Stage == "market" && m.InheritsToContents).Sum(m => m.Percent);

        public static bool? WithinBudget(CustomerPrice price, CustomerBudget budget, bool customerBuysOwnedItem)
            => customerBuysOwnedItem && budget.Enabled && budget.Remaining.HasValue ? (bool?)(price.Value <= budget.Remaining.Value) : null;
    }
}
