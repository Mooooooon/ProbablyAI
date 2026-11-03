using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using UnityEngine;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Labels
{
    // All access is guarded by the shared main-thread snapshot. Mutations use the actual
    // item targeting path followed by the native label-window callbacks, never save edits.
    public sealed class GameLabelBackend : ILabelBackend
    {
        private readonly GameHandsBackend hands;
        public GameLabelBackend(GameHandsBackend hands) => this.hands = hands;

        public LabelOptions Inspect(string toolItemId, string targetItemId)
        {
            var world = hands.Capture();
            var sourceInfo = world.Items.FirstOrDefault(i => i.Id == toolItemId);
            var targetInfo = world.Items.FirstOrDefault(i => i.Id == targetItemId);
            if (sourceInfo == null || targetInfo == null)
                throw new HandsException("item_not_found", T("标签机或目标已不在当前容器树中，请重新查询。"));
            var source = hands.ResolveCurrentItem(toolItemId);
            var target = hands.ResolveCurrentItem(targetItemId);
            if (source == target || source.identifier != "labeler" && source.identifier != "labeler_blue")
                throw new HandsException("wrong_tool", T("需要真实的普通标签打印机或蓝色标签机。"));
            if (source.identifier == "labeler_blue" && target.identifier == "dossier")
                throw new HandsException("rename_rejected", T("原生蓝色标签机不能重命名档案架。"));
            if (GameMaster.current != null && GameMaster.current.InCombat())
                throw new HandsException("in_combat", T("当前场景不能修改标签或名称。"));
            if (world.Containers.Any(c => (c.Id == sourceInfo.ContainerId || c.Id == targetInfo.ContainerId) && c.Locked) ||
                !source.MayTarget(target) || !source.CanTarget(target))
                throw new HandsException("label_rejected", T("游戏不允许这次标签操作，请检查所有权、锁定、目标或当前界面。"));
            var result = new LabelOptions
            {
                Item = targetInfo, ToolItemId = toolItemId,
                Mode = source.identifier == "labeler" ? "labels" : "custom_name",
                CustomName = GeneralHelper.HasCustomName(target) ? GeneralHelper.GetCustomName(target) : null
            };
            if (result.Mode == "custom_name")
            {
                result.NameCharacterLimit = NameLimit();
                return result;
            }
            foreach (var feature in target.itemFeatures)
            {
                if (feature == null || feature.isDisabled || !LabelerHelper.IsEditableLabelFeature(feature)) continue;
                var current = feature.GetCurrentItemCondition(); // Public/exposed, never realCondition.
                result.Labels.Add(new EditableLabel
                {
                    CategoryId = feature.category,
                    Name = ItemFeatureList.CategoryNames.TryGetValue(feature.category, out var title) ? Plain(title) : feature.category,
                    CurrentConditionId = current?.identifier, CurrentDisplay = Plain(current?.display),
                    Exposed = feature.IsExposed(),
                    Choices = ItemConditionList.conditionLookup[feature.category].Select(choice => new LabelChoice
                    { ConditionId = choice.identifier, Display = Plain(choice.display) }).ToList()
                });
            }
            return result;
        }

        public LabelChange SetLabel(string toolItemId, string targetItemId, string categoryId, string conditionId)
        {
            var before = Inspect(toolItemId, targetItemId);
            var label = before.Labels.SingleOrDefault(l => l.CategoryId == categoryId);
            int index = label?.Choices.FindIndex(c => c.ConditionId == conditionId) ?? -1;
            if (before.Mode != "labels" || label == null || index < 0)
                throw new HandsException("invalid_label_option", T("此标签类别或选项当前不可编辑，请重新查询 get_label_options。"));
            var ui = LabelerUIManager.Instance;
            if (ui == null || ui.panel == null || ui.verticalLayoutGroup == null)
                throw new HandsException("label_ui_unavailable", T("当前无法使用标签编辑窗口。"));
            EnsureWindowsIdle();
            var retired = ui.verticalLayoutGroup.transform.Cast<Transform>().ToArray();
            try
            {
                new GameItemUseBackend(hands).Use(toolItemId, targetItemId);
                if (!ui.panel.activeSelf) throw new InvalidOperationException();
                // Native OpenUI schedules old rows for Destroy at frame end. Exclude those
                // rows now, so consecutive tools in one frame cannot select/save stale rows.
                RetireRows(retired, ui.verticalLayoutGroup.transform);
                var element = ui.verticalLayoutGroup.GetComponentsInChildren<LabelerElement>(true)
                    .SingleOrDefault(e => e.GetCategory() == categoryId);
                // Target callbacks may refresh option tables. Resolve the requested ID again
                // rather than trusting an index obtained before the native targeting action.
                index = ItemConditionList.conditionLookup.TryGetValue(categoryId, out var currentChoices)
                    ? currentChoices.FindIndex(choice => choice.identifier == conditionId) : -1;
                if (element == null || element.dropdown == null || index < 0 || index >= element.dropdown.options.Count)
                    throw new InvalidOperationException();
                element.dropdown.SetValueWithoutNotify(index);
                element.OnDropdownValueChanged();
            }
            finally
            {
                if (ui.panel.activeSelf)
                {
                    RetireRows(retired, ui.verticalLayoutGroup.transform);
                    ui.CloseUI();
                }
            }
            var after = Inspect(toolItemId, targetItemId);
            return new LabelChange { Before = before, After = after,
                Changed = before.Labels.Any(old => after.Labels.Any(current => current.CategoryId == old.CategoryId &&
                    (current.CurrentConditionId != old.CurrentConditionId || current.Exposed != old.Exposed))) };
        }

        public LabelChange Rename(string toolItemId, string targetItemId, string name)
        {
            var before = Inspect(toolItemId, targetItemId);
            if (before.Mode != "custom_name")
                throw new HandsException("wrong_tool", T("修改名称需要蓝色标签机 labeler_blue。"));
            var ui = LabelerBlueUIManager.Instance;
            if (ui == null || ui.panel == null || ui.inputField == null)
                throw new HandsException("label_ui_unavailable", T("当前无法使用标签编辑窗口。"));
            EnsureWindowsIdle();
            try
            {
                new GameItemUseBackend(hands).Use(toolItemId, targetItemId);
                if (!ui.panel.activeSelf) throw new InvalidOperationException();
                if (name.Length > NameLimit())
                    throw new HandsException("name_too_long", T("名称超过原生标签机输入框的长度限制，请缩短名称。"));
                ui.inputField.SetTextWithoutNotify(name);
                if (name.Length == 0) ui.OnClearButtonClick();
                else ui.OnEditEnd(name);
            }
            finally
            {
                if (ui.panel.activeSelf)
                {
                    StoreUIManager.Instance.StopTyping();
                    ui.CloseUI();
                }
            }
            var after = Inspect(toolItemId, targetItemId);
            return new LabelChange { Before = before, After = after, Changed = before.CustomName != after.CustomName };
        }

        private static int NameLimit()
        {
            int limit = LabelerBlueUIManager.Instance?.inputField?.characterLimit ?? 0;
            return limit > 0 ? Math.Min(limit, LabelProtocol.MaxNameLength) : LabelProtocol.MaxNameLength;
        }
        private static void EnsureWindowsIdle()
        {
            if (LabelerUIManager.Instance?.panel?.activeSelf == true || LabelerBlueUIManager.Instance?.panel?.activeSelf == true)
                throw new HandsException("label_ui_busy", T("标签编辑窗口正在使用中，请先关闭后再让助手修改。"));
        }
        private static void RetireRows(IEnumerable<Transform> rows, Transform parent)
        {
            foreach (var row in rows)
                if (row != null && row.parent == parent) { row.gameObject.SetActive(false); row.SetParent(null); }
        }
        private static string Plain(string value) => value == null ? null : Regex.Replace(value, "<[^>]*>", "").Trim();
    }
}
