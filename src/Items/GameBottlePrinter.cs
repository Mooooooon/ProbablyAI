using System;
using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    internal sealed class GameBottlePrinter
    {
        private readonly GameHandsBackend hands;
        public GameBottlePrinter(GameHandsBackend hands) => this.hands = hands;

        public ItemInfo Print(string itemId, BottleSize size)
        {
            var before = hands.Capture();
            if (GameMaster.current != null && GameMaster.current.InCombat())
                throw new HandsException("in_combat", T("战斗中不能使用物品，请等战斗结束。"));
            var printer = hands.ResolveCurrentItem(itemId);
            if (printer.identifier != "bottle_printer")
                throw new HandsException("wrong_item_type", T("请选择一台水瓶打印机。"));
            if (!GeneralHelper.IsItemEffectivelyOwned(printer))
                throw new HandsException("printer_not_owned", T("只能使用玩家拥有的水瓶打印机。"));

            var printerInfo = before.Items.First(item => item.Id == itemId);
            var window = printer.contentWindow;
            var machine = before.Containers.FirstOrDefault(container => container.Id == printerInfo.ContentContainerId);
            if (machine?.Locked == true || window?.isLocked == true ||
                printer.IsTag("LOCKED_CONTAINER_TAG") && printer.GetTagReadonly("LOCK_STATUS").GetBool() ||
                printer.parentInventory.IsInsertLocked() && printer.parentInventory.IsRemoveLocked())
                throw new HandsException("container_locked", T("容器已锁定。"));

            var row = window?.childElement as MachineryPanelRow;
            var control = row?.GetPanel("control");
            var buttons = control?.content as MachineryStack;
            var power = (row?.GetPanel("power")?.content as MachineryIntake)?.content as GameSlotInventory;
            var output = (row?.GetPanel("output")?.content as MachineryIntake)?.content as GameGridInventory;
            var outputInfo = before.Containers.FirstOrDefault(container => container.ParentId == machine?.Id &&
                container.OwnerItemId == itemId && container.Role == "output");
            if (before.Truncated || buttons?.elements.Count != 3 || power == null || output == null || outputInfo == null ||
                control.isHidden)
                throw new HandsException("printer_unavailable", T("当前无法访问水瓶打印机的原生控制或成品区。"));
            if (outputInfo.Locked || outputInfo.InsertLocked)
                throw new HandsException("container_locked", T("目标容器已锁定或禁止放入。"));

            var (index, identifier, cost, unlocked) = size switch
            {
                BottleSize.Small => (0, "small_bottled_water", MachineBottlePrinter.SMALL_PRINT_COST, true),
                BottleSize.Normal => (1, "bottled_water", MachineBottlePrinter.NORMAL_PRINT_COST, MachineBottlePrinter.IsNormalUnlocked(printer)),
                BottleSize.Large => (2, "large_bottled_water", MachineBottlePrinter.LARGE_PRINT_COST, MachineBottlePrinter.IsLargeUnlocked(printer)),
                _ => throw new ArgumentOutOfRangeException(nameof(size))
            };
            if (!unlocked)
                throw new HandsException("bottle_size_locked", T("这台水瓶打印机还没解锁该规格。"));
            if (!(buttons.elements[index] is MachineryButton button) || button.isDisabled || button.isHidden ||
                button.isSelected || button.requiresHold)
                throw new HandsException("printer_unavailable", T("当前无法访问水瓶打印机的原生控制或成品区。"));
            var battery = power.childItem;
            if (battery == null || !GeneralHelper.IsItemEffectivelyOwned(battery) || !PowerHelper.CanDrawPowerSource(battery, cost))
                throw new HandsException("printer_no_power", T("水瓶打印机没有可用的电源，或电量不足。"));

            var quantities = before.Items.Where(item => item.ContainerId == outputInfo.Id && item.Identifier == identifier)
                .ToDictionary(item => item.Id, item => item.Quantity);
            WorldSnapshot after;
            try
            {
                // The native button callback performs placement and draws power.
                try { button.Select(); }
                finally { button.Deselect(); }
                after = hands.Capture();
            }
            catch (Exception)
            {
                throw new HandsException("print_unverified", T("未能核实打印结果。请检查成品区和电量，再决定是否重试。"));
            }

            var bottle = after.Items.FirstOrDefault(item => item.ContainerId == outputInfo.Id && item.Identifier == identifier &&
                (!quantities.TryGetValue(item.Id, out int previous) || item.Quantity > previous));
            if (bottle != null) return bottle;
            if (after.Truncated || !after.Containers.Any(container => container.Id == outputInfo.Id && !container.Locked))
                throw new HandsException("print_unverified", T("未能核实打印结果。请检查成品区和电量，再决定是否重试。"));
            throw new HandsException("print_no_output", T("成品区没有新增水瓶。请检查成品区是否有足够空间。"));
        }
    }
}
