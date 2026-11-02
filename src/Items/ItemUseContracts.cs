using System;
using ProbablyAI.Hands;

namespace ProbablyAI.Items
{
    // A read-only view of the source item and the target container. The native backend fills
    // these from the shared fresh snapshot; tests build them directly without Unity.
    public sealed class ItemUseReading
    {
        public ItemInfo Item { get; set; }
        // One of water_tablet / nutrient_tablet / uv_filter / water_ration, or null.
        public string Kind { get; set; }
        public bool Owned { get; set; }
        public bool Locked { get; set; }
        public int Quantity { get; set; }

        public bool LiquidContainer { get; set; }
        public bool SupportedLiquid { get; set; }
        public bool Wine { get; set; }
        public bool ChemicalProduct { get; set; }
        public bool HasWater { get; set; }
        public int VolumeUl { get; set; }
        public int CapacityUl { get; set; }
        // Water rations natively add a fixed volume; other supported items have no fixed amount.
        public int RequiredUl { get; set; }

        public int FreeUl => Math.Max(0, CapacityUl - VolumeUl);
    }

    public sealed class ItemUseChange
    {
        public ItemUseReading SourceBefore { get; set; }
        public ItemUseReading SourceAfter { get; set; }
        public ItemUseReading TargetBefore { get; set; }
        public ItemUseReading TargetAfter { get; set; }
        public bool SourceConsumed { get; set; }
        // True when the liquid parts actually changed, even if the total volume stayed the same.
        public bool LiquidChanged { get; set; }
    }

    public interface IItemUseBackend
    {
        ItemUseChange Use(string sourceItemId, string targetItemId);
    }

    public sealed class ItemUseValidation
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
