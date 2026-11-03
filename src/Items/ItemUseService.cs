using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    // Validation and result mapping for the general non-combat item actions. The native
    // backend performs the actual target/activate action and reports what it observed.
    public sealed class ItemUseService
    {
        private readonly IItemUseBackend backend;
        public ItemUseService(IItemUseBackend backend) => this.backend = backend;

        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in ItemUseProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => Execute(name, arguments), changesState: true);
            }
        }

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                switch (tool)
                {
                    case "use_item_on_item":
                        ValidateKeys(arguments, "item_id", "target_item_id");
                        var sourceId = Id(arguments, "item_id");
                        var targetId = Id(arguments, "target_item_id");
                        if (sourceId == targetId) throw new HandsException("same_item", T("不能对物品自身使用。"));
                        return Result(backend.Use(sourceId, targetId), activate: false);
                    case "activate_item":
                        ValidateKeys(arguments, "item_id");
                        return Result(backend.Activate(Id(arguments, "item_id")), activate: true);
                    default:
                        return HandsResult.Fail("unknown_tool", T("未知的物品使用工具。"));
                }
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("item_use_failed", T("物品使用未完成，请刷新物品后重试。不要重复执行未核实的操作。")); }
        }

        private static HandsResult Result(ItemUseChange change, bool activate)
        {
            RequireChange(change, hasTarget: !activate);
            bool consumed = change.SourceConsumed ||
                change.SourceAfter != null && change.SourceAfter.Quantity < change.SourceBefore.Quantity;
            bool changed = consumed || change.Changed;
            string kind = change.SourceBefore.Kind ?? "";
            string message = activate ? ActivationMessage(change, consumed) : UseMessage(change, kind, consumed);
            object target = change.TargetBefore == null ? null : TargetData(change);
            return new HandsResult
            {
                Success = true,
                Code = "ok",
                Message = message,
                Data = new
                {
                    effect = string.IsNullOrEmpty(kind) ? "item" : kind,
                    source = new
                    {
                        item_id = change.SourceBefore.Item.Id,
                        name = change.SourceBefore.Item.Name,
                        quantity_before = change.SourceBefore.Quantity,
                        quantity_after = change.SourceAfter?.Quantity,
                        consumed
                    },
                    target,
                    changed
                }
            };
        }

        private static object TargetData(ItemUseChange change)
        {
            var after = change.TargetAfter;
            var before = change.TargetBefore;
            return new
            {
                item_id = after?.Item.Id ?? before.Item.Id,
                name = after?.Item.Name ?? before.Item.Name,
                path = after?.Item.Path ?? before.Item.Path,
                volume_before_ml = Ml(before.VolumeUl),
                volume_ml = after == null ? (double?)null : Ml(after.VolumeUl),
                capacity_ml = Ml(before.CapacityUl)
            };
        }

        private static string UseMessage(ItemUseChange change, string kind, bool consumed)
        {
            string target = change.TargetAfter?.Item.Name ?? change.TargetBefore.Item.Name;
            string source = change.SourceBefore.Item.Name;
            string status = consumed ? T("已被原生行为消耗") : T("仍保留");
            var after = change.TargetAfter;
            bool hasVolume = after != null && after.LiquidContainer;
            switch (kind)
            {
                case ItemUseRules.WaterRation:
                    return F("已把 {0} 加入 {1}；水配给{2}。容器现有 {3}/{4} ml。",
                        source, target, status, Ml(after?.VolumeUl ?? 0), Ml(after?.CapacityUl ?? 0));
                case ItemUseRules.UVFilter:
                    return F("已对 {0} 使用 {1}；UV 过滤器{2}。容器现有 {3}/{4} ml。水质请用试纸、扫描或 analyze_water 确认。",
                        target, source, status, Ml(after?.VolumeUl ?? 0), Ml(after?.CapacityUl ?? 0));
                case ItemUseRules.NutrientTablet:
                    return F("已对 {0} 使用 {1}；营养药片{2}。容器现有 {3}/{4} ml。原生反应可能需要时间，水质 / 养分请用试纸、扫描或 analyze_water 确认。",
                        target, source, status, Ml(after?.VolumeUl ?? 0), Ml(after?.CapacityUl ?? 0));
                case ItemUseRules.WaterTablet:
                    return F("已对 {0} 使用 {1}；净水药片{2}。容器现有 {3}/{4} ml。净化反应可能需要时间，水质请用试纸、扫描或 analyze_water 确认。",
                        target, source, status, Ml(after?.VolumeUl ?? 0), Ml(after?.CapacityUl ?? 0));
                default:
                    string volume = hasVolume
                        ? F(" 目标现有 {0}/{1} ml。", Ml(after.VolumeUl), Ml(after.CapacityUl))
                        : "";
                    return F("已对 {0} 使用 {1}；原生操作已执行，源物品{2}。{3}{4}",
                        target, source, status, volume, T("如果游戏打开了相关界面，请按原生提示继续。"));
            }
        }

        private static string ActivationMessage(ItemUseChange change, bool consumed)
        {
            string source = change.SourceBefore.Item.Name;
            string status = consumed ? T("已被原生行为消耗") : T("仍保留");
            return F("已使用 {0}；原生操作已执行，物品{1}。{2}",
                source, status, T("如果游戏打开了相关界面，请按原生提示继续。"));
        }

        private static double Ml(int microliters) => Math.Round(microliters / 1000d, 2);

        private static void RequireChange(ItemUseChange change, bool hasTarget)
        {
            if (change?.SourceBefore == null || change.SourceBefore.Item == null ||
                hasTarget && (change.TargetBefore == null || change.TargetBefore.Item == null))
                throw new HandsException("item_use_failed", T("物品使用未完成，请刷新物品后重试。不要重复执行未核实的操作。"));
        }

        private static void ValidateKeys(JObject arguments, params string[] keys)
        {
            if (arguments == null) throw new HandsException("invalid_arguments", T("参数必须是 JSON 对象。"));
            if (arguments.Properties().Any(property => !keys.Contains(property.Name)))
                throw new HandsException("invalid_arguments", T("包含不支持的参数。"));
        }

        private static string Id(JObject arguments, string name)
        {
            var value = arguments?[name];
            if (value == null || value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value) ||
                ((string)value).Length > 200)
                throw new HandsException("invalid_arguments", T("物品使用需要先前查询返回的有效物品 ID，且不接受其他参数。"));
            return ((string)value).Trim();
        }
    }
}
