using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Liquids
{
    // Native adapter. It uses the game's own item-on-item target action (the same path as
    // dragging one item onto another) and measures the real liquid volume before and after.
    public sealed class GameLiquidBackend : ILiquidBackend
    {
        private readonly GameHandsBackend hands;
        public GameLiquidBackend(GameHandsBackend hands) => this.hands = hands;

        public LiquidReading Inspect(string itemId)
        {
            var info = Find(hands.Capture(), itemId);
            return Read(info, hands.ResolveCurrentItem(itemId));
        }

        public LiquidChange Pour(string sourceItemId, string targetItemId, bool requireFullTransfer = false)
        {
            var world = hands.Capture();
            var sourceInfo = Find(world, sourceItemId);
            var targetInfo = Find(world, targetItemId);
            var source = hands.ResolveCurrentItem(sourceItemId);
            var target = hands.ResolveCurrentItem(targetItemId);
            var sourceBefore = Read(sourceInfo, source);
            var targetBefore = Read(targetInfo, target);
            Reject(LiquidRules.ValidatePour(sourceBefore, targetBefore, requireFullTransfer));
            void RecheckFullTransfer()
            {
                sourceBefore = Read(sourceInfo, source);
                targetBefore = Read(targetInfo, target);
                Reject(LiquidRules.ValidatePour(sourceBefore, targetBefore, true));
            }
            UseOn(source, target, requireFullTransfer ? (System.Action)RecheckFullTransfer : null);
            return LiquidChange.MeasurePour(sourceBefore, Read(sourceInfo, source), targetBefore, Read(targetInfo, target));
        }

        public LiquidChange Dump(string itemId)
        {
            var world = hands.Capture();
            var info = Find(world, itemId);
            var item = hands.ResolveCurrentItem(itemId);
            var before = Read(info, item);
            Reject(LiquidRules.ValidateDump(before));
            var entry = EmporiumEntry.Instance;
            var drain = entry == null ? null : entry.drain;
            if (drain == null)
                throw new HandsException("store_unavailable", T("当前无法使用排水口，请返回当铺并等待载入完成。"));
            UseOn(item, drain);
            return LiquidChange.MeasureDump(before, Read(info, item));
        }

        public LiquidChange Fill(string itemId)
        {
            var world = hands.Capture();
            var info = Find(world, itemId);
            var item = hands.ResolveCurrentItem(itemId);
            var before = Read(info, item);
            Reject(LiquidRules.ValidateFill(before));
            var entry = EmporiumEntry.Instance;
            var faucet = entry == null ? null : entry.faucet;
            if (faucet == null)
                throw new HandsException("store_unavailable", T("当前无法使用当铺水龙头，请返回当铺并等待载入完成。"));
            if (!EmporiumEntry.IsWaterTapUnlocked())
                throw new HandsException("tap_locked", T("当铺水龙头尚未解锁，无法接水。请先在网络升级中解锁锈水或下水道水龙头。"));
            UseOn(item, faucet);
            return LiquidChange.MeasureFill(before, Read(info, item));
        }

        private static ItemInfo Find(WorldSnapshot world, string itemId)
            => world.Items.FirstOrDefault(item => item.Id == itemId)
                ?? throw new HandsException("item_not_found", T("物品已不在当前容器树中。"));

        private static LiquidReading Read(ItemInfo info, GameItem item)
        {
            bool container = item != null && item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);
            bool hasCapacity = container && item.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY);
            int capacity = hasCapacity ? item.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt() : 0;
            bool supported = hasCapacity && capacity > 0;
            bool wine = container && item.IsTag("WINE_CONTAINER_TAG");
            return new LiquidReading
            {
                Item = info,
                Container = container,
                Supported = supported,
                Owned = item != null && GeneralHelper.IsItemEffectivelyOwned(item),
                Dropper = container && item.IsTag(WaterHelper.LIQUID_CONTAINER_DROPPER_TAG),
                Wine = wine,
                WineReady = wine && WineHelper.IsFinishedWine(item),
                VolumeUl = supported ? WaterHelper.GetTotalVolume(item) : 0,
                CapacityUl = capacity
            };
        }

        private static void Reject(LiquidValidation validation)
        {
            if (validation != null) throw new HandsException(validation.Code, validation.Message);
        }

        private static void UseOn(GameItem source, GameItem target, System.Action beforeTarget = null)
        {
            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("liquid_action_rejected", T("游戏不允许这次液体操作：请检查所有权、锁定、容器状态或当前界面。"));
            // Mirror the native drag path so registered target callbacks from other mods are
            // not skipped; then recheck before the real item action.
            if (ItemBehaviourManager.current != null)
                ItemBehaviourManager.current.Target(source, target);
            if (!source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("liquid_action_rejected", T("游戏不允许这次液体操作：请检查所有权、锁定、容器状态或当前界面。"));
            beforeTarget?.Invoke();
            source.Target(target);
        }
    }
}
