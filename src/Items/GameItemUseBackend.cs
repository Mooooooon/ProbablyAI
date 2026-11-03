using System.Collections.Generic;
using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    // Native adapter for the general non-combat item actions. The game's own target/activate
    // gates decide everything; this adapter only enforces non-combat and reports observations.
    public sealed class GameItemUseBackend : IItemUseBackend
    {
        private readonly GameHandsBackend hands;
        public GameItemUseBackend(GameHandsBackend hands) => this.hands = hands;

        public ItemUseChange Use(string sourceItemId, string targetItemId)
        {
            EnsureOutOfCombat();
            var world = hands.Capture();
            var sourceInfo = Find(world, sourceItemId);
            var targetInfo = Find(world, targetItemId);
            var source = hands.ResolveCurrentItem(sourceItemId);
            var target = hands.ResolveCurrentItem(targetItemId);
            var sourceBefore = Read(world, sourceInfo, source);
            var targetBefore = Read(world, targetInfo, target);
            Reject(ItemUseRules.ValidateUse(sourceBefore, targetBefore));

            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("item_use_rejected", T("游戏不允许这次物品使用：请检查所有权、锁定、目标、容量或当前界面。"));
            var partsBefore = targetBefore.SupportedLiquid ? WaterHelper.GetLiquidParts(target) : null;
            // Mirror the native drag path so other mods' target callbacks are not skipped.
            if (ItemBehaviourManager.current != null)
                ItemBehaviourManager.current.Target(source, target);
            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("item_use_rejected", T("游戏不允许这次物品使用：请检查所有权、锁定、目标、容量或当前界面。"));
            source.Target(target);

            var afterWorld = hands.Capture();
            var sourceAfterInfo = afterWorld.Items.FirstOrDefault(item => item.Id == sourceItemId);
            var targetAfterInfo = afterWorld.Items.FirstOrDefault(item => item.Id == targetItemId);
            var sourceAfter = sourceAfterInfo == null ? null : Read(afterWorld, sourceAfterInfo, hands.ResolveCurrentItem(sourceItemId));
            var targetAfter = targetAfterInfo == null ? null : Read(afterWorld, targetAfterInfo, hands.ResolveCurrentItem(targetItemId));
            bool consumed = sourceAfter == null || sourceAfter.Quantity < sourceBefore.Quantity;
            bool partsChanged = partsBefore != null && targetAfter != null &&
                !partsBefore.SequenceEqual(WaterHelper.GetLiquidParts(hands.ResolveCurrentItem(targetItemId)));
            return new ItemUseChange
            {
                SourceBefore = sourceBefore, SourceAfter = sourceAfter,
                TargetBefore = targetBefore, TargetAfter = targetAfter,
                SourceConsumed = consumed,
                Changed = consumed || SnapshotChanged(sourceBefore, sourceAfter) ||
                    SnapshotChanged(targetBefore, targetAfter) || partsChanged
            };
        }

        public ItemUseChange Activate(string itemId)
        {
            EnsureOutOfCombat();
            var world = hands.Capture();
            var info = Find(world, itemId);
            var item = hands.ResolveCurrentItem(itemId);
            var before = Read(world, info, item);
            Reject(ItemUseRules.ValidateActivate(before));
            if (!item.MayActivateSlotItem() || !item.CanActivateSlotItem())
                throw new HandsException("item_use_rejected", T("游戏不允许使用这个物品：请检查所有权、位置或当前界面。"));
            item.ActivateSlotItem();

            var afterWorld = hands.Capture();
            var afterInfo = afterWorld.Items.FirstOrDefault(entry => entry.Id == itemId);
            var after = afterInfo == null ? null : Read(afterWorld, afterInfo, hands.ResolveCurrentItem(itemId));
            bool consumed = after == null || after.Quantity < before.Quantity;
            return new ItemUseChange
            {
                SourceBefore = before, SourceAfter = after,
                SourceConsumed = consumed,
                Changed = consumed || SnapshotChanged(before, after)
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
            var owner = world.Containers.FirstOrDefault(containerInfo => containerInfo.Id == info.ContainerId);
            return new ItemUseReading
            {
                Item = info,
                Kind = Kind(item),
                Locked = owner != null && owner.Locked,
                Combat = IsCombatItem(item),
                Enemy = item.IsTag("enemy"),
                Weapon = item.IsGameItemType("WEAPON"),
                Animal = item.IsTag("ANIMAL_ITEM_TAG"),
                Quantity = item.unitCount,
                LiquidContainer = container,
                SupportedLiquid = hasCapacity && capacity > 0,
                HasWater = container && WaterPart(item) > 0,
                VolumeUl = hasCapacity && capacity > 0 ? WaterHelper.GetTotalVolume(item) : 0,
                CapacityUl = capacity
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

        private static int WaterPart(GameItem item)
        {
            var tag = item.GetTagReadonly(Liquid.GetCurrentPartFromId("water"));
            return tag == null ? 0 : tag.GetInt();
        }

        private static bool IsCombatItem(GameItem item)
        {
            if (item == null) return false;
            return item.IsGameItemType("WEAPON") || item.IsGameItemType("AMMUNITION") ||
                item.IsGameItemType("EXPLOSIVE") || item.IsTag("ability") || item.IsTag("enemy");
        }

        private static void EnsureOutOfCombat()
        {
            try
            {
                if (GameMaster.current != null && GameMaster.current.InCombat())
                    throw new HandsException("in_combat", T("战斗中不能使用物品，请等战斗结束。"));
            }
            catch (HandsException) { throw; }
            catch (System.Exception) { }
        }

        private static bool SnapshotChanged(ItemUseReading before, ItemUseReading after)
        {
            if (before == null || after == null) return before != after;
            var first = before.Item;
            var second = after.Item;
            return first.Name != second.Name || first.Description != second.Description ||
                first.Quantity != second.Quantity || first.ContainerId != second.ContainerId ||
                first.ContentContainerId != second.ContentContainerId || first.Owned != second.Owned ||
                first.Removable != second.Removable ||
                !(first.Types ?? new List<string>()).SequenceEqual(second.Types ?? new List<string>()) ||
                before.Kind != after.Kind || before.LiquidContainer != after.LiquidContainer;
        }

        private static void Reject(ItemUseValidation validation)
        {
            if (validation != null) throw new HandsException(validation.Code, validation.Message);
        }
    }
}
