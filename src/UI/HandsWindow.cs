using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ProbablyAI.Hands;
using ProbablyAI.Services;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.UI
{
    public sealed class HandsWindow : IDisposable
    {
        private readonly HandsService hands;
        private readonly HandsAgent agent;
        private readonly ConnectionSettingsStore connection;
        private readonly List<ContainerInfo> sources = new List<ContainerInfo>();
        private readonly List<ContainerInfo> destinations = new List<ContainerInfo>();
        private readonly List<ItemInfo> items = new List<ItemInfo>();
        private readonly StringBuilder output = new StringBuilder();
        private CustomUIWindow window;
        private WorldSnapshot snapshot;
        private CancellationTokenSource pending;
        private bool busy;
        public bool IsOpen => window != null && window.IsAlive && window.IsVisible;

        public HandsWindow(HandsService hands, HandsAgent agent, ConnectionSettingsStore connection)
        {
            this.hands = hands; this.agent = agent; this.connection = connection;
        }

        public void Show()
        {
            var b = CustomUIManager.Window(ModEntry.ModId + ".hands", Branding.DisplayShortName + T(" · 物品与容器"), CustomUIManager.LAYER_PAPER)
                .SetSize(960f, 850f).Center().SetDraggable(true).SetCloseOnEscape();
            window = b.Handle;
            window.root.SetActive(false);
            b.OnClose(OnClosed)
                .BeginTabs()
                    .BeginTab(T("手动操作"))
                        .BeginScroll(470f)
                            .AddButton(T("刷新容器与物品"), Refresh, "refresh")
                            .AddLabel(T("来源范围（路径包含嵌套容器和机器分区）"))
                            .AddDropdown(new[] { T("载入中…") }, 0, _ => RefreshItems(), "source")
                            .AddToggle(T("包含子容器中的物品"), true, _ => RefreshItems(), "nested")
                            .AddLabel(T("选择物品"))
                            .AddDropdown(new[] { T("请选择物品") }, 0, _ => ItemDetails(), "item")
                            .AddLabel(T("选择物品后显示当前位置、数量和所有权。"), "details")
                            .AddLabel(T("目标容器 / 分区"))
                            .AddDropdown(new[] { T("载入中…") }, 0, null, "destination")
                            .AddLabel(T("数量：0 = 所选这一堆全部"))
                            .AddInput("0", "0", id: "quantity")
                            .BeginRow()
                                .AddButton(T("预检移动"), () => Transfer(true), "preview")
                                .AddButton(T("移动物品"), () => Transfer(false), "move")
                                .AddButton(T("打开来源窗口"), () => SetSourceOpen(true), "open")
                                .AddButton(T("关闭来源窗口"), () => SetSourceOpen(false), "hide")
                            .End()
                        .End()
                    .End()
                    .BeginTab(T("助手指令"))
                        .BeginScroll(470f)
                            .AddLabel(T("使用 F7 中已保存的连接配置。点击执行后，助手会查询并操作当前当铺的物品。"))
                            .AddInput(T("例如：把窗口里的普通矿石拿到柜台；把档案架中导师笔记里的某份笔记拿到柜台。"), "", id: "instruction")
                            .BeginRow()
                                .AddButton(T("执行助手指令"), RunInstruction, "run")
                                .AddButton(T("停止"), Cancel, "stop")
                            .End()
                            .AddLabel(T("支持上柜台、从客人窗口拿取、入库、归档、投放垃圾桶和机器投料。只按游戏规则移动；每次指令独立处理。"))
                        .End()
                    .End()
                .EndTabs()
                .AddLabel(T("操作记录 / 助手回复"))
                .BeginScroll(145f).AddLabel(T("尚未执行操作。"), "output").End()
                .AddLabel(T("准备就绪。"), "status")
                .BeginRow()
                    .AddButton(T("清空显示记录"), () => { output.Clear(); window.Get("output").SetText(T("记录已清空。")); })
                    .AddButton(T("关闭"), Dispose)
                .End();

            foreach (var id in new[] { "details", "output", "status" })
            {
                var text = window.Get(id)?.text;
                if (text != null) { text.richText = false; text.enableWordWrapping = true; }
            }
            var instruction = window.Get("instruction");
            instruction.input.lineType = TMP_InputField.LineType.MultiLineNewline;
            instruction.input.characterLimit = 6000;
            instruction.input.richText = false;
            instruction.input.textComponent.richText = false;
            instruction.SetPreferredSize(-1f, 110f);
            var quantity = window.Get("quantity");
            quantity.input.characterLimit = 10;
            quantity.input.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantity.input.lineType = TMP_InputField.LineType.SingleLine;
            foreach (var id in new[] { "source", "item", "destination" })
            {
                var dropdown = window.Get(id).dropdown;
                dropdown.captionText.richText = false;
                dropdown.itemText.richText = false;
                dropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;
                window.Get(id).SetPreferredSize(-1f, 34f);
            }
            SetBusy(false);
            b.Show();
            Refresh();
            foreach (var line in hands.History) Append(line);
        }

        private ContainerInfo Source => Selected(sources, "source");
        private ContainerInfo Destination => Selected(destinations, "destination");
        private ItemInfo SelectedItem
        {
            get
            {
                int index = window.Get("item").GetIndex() - 1;
                return index >= 0 && index < items.Count ? items[index] : null;
            }
        }
        private ContainerInfo Selected(List<ContainerInfo> list, string id)
        {
            int index = window.Get(id).GetIndex();
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        private void Refresh()
        {
            if (!IsOpen) return;
            try
            {
                var sourceId = Source?.Id ?? "inventory";
                var destinationId = Destination?.Id ?? "counter";
                snapshot = hands.Snapshot();
                sources.Clear(); sources.AddRange(snapshot.Containers);
                destinations.Clear(); destinations.AddRange(snapshot.Containers.Where(c => c.Kind != "group"));
                window.Get("source").SetOptions(sources.Count == 0 ? new[] { T("没有可用容器") } : sources.Select(c => c.Path + (c.Locked ? T("（锁定）") : "")).ToArray())
                    .SetIndex(Math.Max(0, sources.FindIndex(c => c.Id == sourceId)));
                window.Get("destination").SetOptions(destinations.Count == 0 ? new[] { T("没有可用容器") } : destinations.Select(c => c.Path).ToArray())
                    .SetIndex(Math.Max(0, destinations.FindIndex(c => c.Id == destinationId)));
                RefreshItems();
                Status(F("已读取 {0} 个容器 / 分区、{1} 个物品堆。", snapshot.Containers.Count, snapshot.Items.Count) + (snapshot.Truncated ? T("（列表达到上限）") : ""));
            }
            catch (HandsException error) { Status(error.Message); }
            catch (Exception) { Status(T("无法读取容器，请回到当铺后刷新。")); }
        }

        private void RefreshItems()
        {
            if (snapshot == null || Source == null) return;
            var source = Source;
            bool nested = window.Get("nested").GetBool();
            var allowed = new HashSet<string> { source.Id };
            if (nested)
            {
                bool changed;
                do
                {
                    changed = false;
                    foreach (var c in snapshot.Containers)
                        if (c.ParentId != null && allowed.Contains(c.ParentId)) changed |= allowed.Add(c.Id);
                } while (changed);
            }
            items.Clear(); items.AddRange(snapshot.Items.Where(i => allowed.Contains(i.ContainerId)));
            var labels = new List<string> { T("请选择具体物品") };
            labels.AddRange(items.Select(i => i.Name + " ×" + i.Quantity + " — " + i.Path + " [" + i.Id + "]"));
            // Reset item selection after a refresh: never silently arm a different same-name item.
            window.Get("item").SetOptions(labels).SetIndex(0);
            ItemDetails();
        }

        private void ItemDetails()
        {
            var item = SelectedItem;
            window.Get("details").SetText(item == null ? T("请选择物品；嵌套物品会显示完整路径。") :
                F("{0}\n数量：{1}    {2}    {3}\n{4}", item.Path, item.Quantity, (item.Owned ? T("自有") : T("非自有")), (item.Removable ? T("可尝试移动") : T("当前禁止移动")), item.Description));
        }

        private void Transfer(bool preview)
        {
            if (busy) return;
            var item = SelectedItem;
            var target = Destination;
            if (item == null || target == null) { Status(T("请先选择物品和目标容器。")); return; }
            if (!int.TryParse(window.Get("quantity").GetString(), out var quantity) || quantity < 0)
            { Status(T("数量应为非负整数，0 代表所选这一堆全部。")); return; }
            var result = hands.Execute(preview ? "can_move_item" : "move_item", new JObject
            {
                ["item_id"] = item.Id, ["source_container_id"] = item.ContainerId,
                ["destination_container_id"] = target.Id, ["quantity"] = quantity
            });
            Append((preview ? T("预检：") : T("移动：")) + result.Message);
            if (!preview) Refresh();
            Status(result.Message);
        }

        private void SetSourceOpen(bool open)
        {
            if (busy || Source == null) return;
            var result = hands.Execute(open ? "open_container" : "close_container", new JObject { ["container_id"] = Source.Id });
            Append(result.Message); Status(result.Message);
        }

        private async void RunInstruction()
        {
            if (busy) return;
            var instruction = window.Get("instruction").GetString();
            if (string.IsNullOrWhiteSpace(instruction)) { Status(T("请先输入要执行的指令。")); return; }
            var request = new CancellationTokenSource();
            pending = request;
            SetBusy(true);
            Append(T("你：") + instruction);
            try
            {
                var result = await agent.RunAsync(connection.Current, instruction, text => { Status(text); Append(text); }, request.Token);
                if (!IsOpen) return;
                Append(T("小鲸：") + result.Reply);
                Refresh();
                Status(F("本次处理完成，共调用 {0} 次操作接口。实际移动见记录。", result.ToolCalls));
            }
            catch (OperationCanceledException) { Append(T("指令已停止。已完成的移动保留，请刷新查看。")); Status(T("已停止。")); }
            catch (ApiRequestException error) { Append(error.Message); Status(error.Message); }
            catch (HandsException error) { Append(error.Message); Status(error.Message); }
            catch (ArgumentException error) { Append(error.Message); Status(error.Message); }
            catch (Exception) { Append(T("指令未完成，请检查操作记录和当前物品位置。")); }
            finally
            {
                if (pending == request) pending = null;
                request.Dispose();
                if (IsOpen) SetBusy(false);
            }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            foreach (var id in new[] { "refresh", "source", "nested", "item", "destination", "quantity", "preview", "move", "open", "hide", "run", "instruction" })
                window.Get(id)?.SetInteractable(!value);
            window.Get("stop")?.SetInteractable(value);
        }

        private void Append(string text)
        {
            if (!IsOpen) return;
            output.AppendLine(text);
            if (output.Length > 24000) output.Remove(0, output.Length - 18000);
            window.Get("output").SetText(output.ToString());
        }
        private void Status(string text) { if (IsOpen) window.Get("status").SetText(text); }
        private void Cancel() => pending?.Cancel();
        private void OnClosed() { Cancel(); window = null; }
        public void Dispose() { Cancel(); var closing = window; window = null; closing?.Close(); }
    }
}
