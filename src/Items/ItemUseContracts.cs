using System;
using ProbablyAI.Hands;

namespace ProbablyAI.Items
{
    // A read-only view of an item resolved from the shared snapshot. The native backend fills
    // these before and after the action; tests build them directly without Unity.
    public sealed class ItemUseReading
    {
        public ItemInfo Item { get; set; }
        // Optional friendly label for known water-treatment items; null for generic items.
        public string Kind { get; set; }
        public bool Locked { get; set; }
        public bool Combat { get; set; }
        public bool Enemy { get; set; }
        public bool Weapon { get; set; }
        public bool Animal { get; set; }
        public int Quantity { get; set; }

        public bool LiquidContainer { get; set; }
        public bool SupportedLiquid { get; set; }
        public bool HasWater { get; set; }
        public int VolumeUl { get; set; }
        public int CapacityUl { get; set; }
    }

    public sealed class ItemUseChange
    {
        public ItemUseReading SourceBefore { get; set; }
        public ItemUseReading SourceAfter { get; set; }
        public ItemUseReading TargetBefore { get; set; }
        public ItemUseReading TargetAfter { get; set; }
        public bool SourceConsumed { get; set; }
        // True when the native action visibly changed the source, target or liquid state.
        public bool Changed { get; set; }
    }

    public interface IItemUseBackend
    {
        ItemUseChange Use(string sourceItemId, string targetItemId);
        ItemUseChange Activate(string itemId);
    }

    public sealed class ItemUseValidation
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
