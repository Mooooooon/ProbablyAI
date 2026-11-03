using System.Linq;

namespace ProbablyAI.Hands
{
    // Called by Capture on Unity's main thread. Read only the durability the player can see.
    internal static class GameItemDurability
    {
        internal static ItemDurability Read(GameItem item)
        {
            if (item == null || !item.IsTag("DURABILITY_TAG")) return null;
            var feature = item.itemFeatures?.FirstOrDefault(f => f != null && f.identifier == "DURABILITY_TAG" && !f.isDisabled);
            if (feature == null) return null;
            bool tested = feature.IsFeatureDiscovered() || feature.IsExposed();
            if (!tested && (!feature.IsInitiallyShown() || feature.GetPublicHidden())) return null;
            // Numeric tags need not be enabled; mirror DurabilityHelper's getters.
            // Never fall back to real durability for an untested item.
            string currentTag = tested ? "DURABILITY_TAG" : "DURABILITY_FAKE_TAG";
            int current = item.GetTagReadonly(currentTag).GetInt();
            int maximum = item.GetTagReadonly("MAX_DURABILITY_TAG").GetInt();
            if (maximum <= 0 || current < 0 || current > maximum) return null;
            return new ItemDurability
            {
                Current = current, Maximum = maximum,
                AssessmentSource = tested ? "tested" : "displayed"
            };
        }
    }
}
