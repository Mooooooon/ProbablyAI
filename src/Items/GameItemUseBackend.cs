using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    // Native adapter for the safe item-on-container actions. It resolves both items from the
    // shared fresh snapshot, runs the game's own target action, then measures what really
    // happened instead of assuming the item was consumed or the water changed.
    public sealed class GameItemUseBackend : IItemUseBackend
    {
        private readonly GameHandsBackend hands;
        public GameItemUseBackend(GameHandsBackend hands) => this.hands = hands;

        public ItemUseChange Use(string sourceItemId, string targetItemId)
        {
            var world = hands.Capture();
            var sourceInfo = Find(world, sourceItemId);
            var targetInfo = Find(world, targetItemId);
            var source = hands.ResolveCurrentItem(sourceItemId);
            var target = hands.ResolveCurrentItem(targetItemId);
            var sourceBefore = Read(world, sourceInfo, source);
            var targetBefore = Read(world, targetInfo, target);
            Reject(ItemUseRules.Validate(sourceBefore, targetBefore));

            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("item_use_rejected", T("游戏不允许这次物品使用：请检查所有权、锁定、目标容器、容量或当前界面。"));
            var partsBefore = WaterHelper.GetLiquidParts(target);
            // Match the native drag path so other mods' target callbacks are not skipped.
            if (ItemBehaviourManager.current != null)
                ItemBehaviourManager.current.Target(source, target);
            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("item_use_rejected", T("游戏不允许这次物品使用：请检查所有权、锁定、目标容器、容量或当前界面。"));
            source.Target(target);

            var afterWorld = hands.Capture();
            var targetAfterInfo = afterWorld.Items.FirstOrDefault(item => item.Id == targetItemId)
                ?? throw new HandsException("item_use_failed", T("使用后目标容器已不可访问，请刷新物品后核实。"));
            var targetAfterItem = hands.ResolveCurrentItem(targetItemId);
            var targetAfter = Read(afterWorld, targetAfterInfo, targetAfterItem);
            var sourceAfterInfo = afterWorld.Items.FirstOrDefault(item => item.Id == sourceItemId);
            var sourceAfter = sourceAfterInfo == null
                ? null
                : Read(afterWorld, sourceAfterInfo, hands.ResolveCurrentItem(sourceItemId));
            var partsAfter = WaterHelper.GetLiquidParts(targetAfterItem);
            return new ItemUseChange
            {
                SourceBefore = sourceBefore, SourceAfter = sourceAfter,
                TargetBefore = targetBefore, TargetAfter = targetAfter,
                SourceConsumed = sourceAfter == null || sourceAfter.Quantity < sourceBefore.Quantity,
                LiquidChanged = !partsBefore.SequenceEqual(partsAfter)
            };
        }

        private static ItemInfo Find(WorldSnapshot world, string itemId)
            => world.Items.FirstOrDefault(item => item.Id == itemId)
                ?? throw new HandsException("item_not_found", T("物品已不在当前容器树中。"));

        private static ItemUseReading Read(WorldSnapshot world, ItemInfo info, GameItem item)
        {
            bool container = item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);
            bool hasCapacity = container && item.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY);
            int capacity = hasCapacity ? item.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt() : 0;
            var owner = world.Containers.FirstOrDefault(c => c.Id == info.ContainerId);
            string kind = Kind(item);
            return new ItemUseReading
            {
                Item = info,
                Kind = kind,
                Owned = GeneralHelper.IsItemEffectivelyOwned(item),
                Locked = owner != null && owner.Locked,
                Quantity = item.unitCount,
                LiquidContainer = container,
                SupportedLiquid = hasCapacity && capacity > 0,
                Wine = container && item.IsTag("WINE_CONTAINER_TAG"),
                ChemicalProduct = container && item.IsTag("CHEMICAL_PRODUCT_TAG"),
                HasWater = container && WaterPart(item) > 0,
                VolumeUl = hasCapacity && capacity > 0 ? WaterHelper.GetTotalVolume(item) : 0,
                CapacityUl = capacity,
                RequiredUl = kind == ItemUseRules.WaterRation ? RationUl(item.identifier) : 0
            };
        }

        private static string Kind(GameItem item)
        {
            if (item.IsTag("WATER_PURIFICATION_TABLET")) return ItemUseRules.WaterTablet;
            if (item.IsTag("NUTRIENT_TABLET")) return ItemUseRules.NutrientTablet;
            if (item.identifier == "uv_filter" && item.IsTag("WATER_FILTER")) return ItemUseRules.UVFilter;
            if (item.IsGameItemType("WATER_RATION")) return ItemUseRules.WaterRation;
            return null;
        }

        private static int RationUl(string identifier)
        {
            if (identifier == "water_ration") return 750000;
            if (identifier == "water_ration_small") return 375000;
            return 0;
        }

        private static int WaterPart(GameItem item)
        {
            var tag = item.GetTagReadonly(Liquid.GetCurrentPartFromId("water"));
            return tag == null ? 0 : tag.GetInt();
        }

        private static void Reject(ItemUseValidation validation)
        {
            if (validation != null) throw new HandsException(validation.Code, validation.Message);
        }
    }
}
