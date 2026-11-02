using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
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
                if (tool != "use_item_on_item")
                    return HandsResult.Fail("unknown_tool", T("未知的物品使用工具。"));
                ValidateKeys(arguments, "item_id", "target_item_id");
                var sourceId = Id(arguments, "item_id");
                var targetId = Id(arguments, "target_item_id");
                if (sourceId == targetId) throw new HandsException("same_item", T("不能对物品自身使用。"));
                return Result(backend.Use(sourceId, targetId));
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("item_use_failed", T("物品使用未完成，请刷新物品后重试。不要重复执行未核实的操作。")); }
        }

        private static HandsResult Result(ItemUseChange change)
        {
            if (change?.SourceBefore == null || change.TargetBefore == null || change.TargetAfter == null)
                throw new HandsException("item_use_failed", T("物品使用未完成，请刷新物品后重试。不要重复执行未核实的操作。"));
            bool consumed = change.SourceConsumed ||
                change.SourceAfter != null && change.SourceAfter.Quantity < change.SourceBefore.Quantity;
            bool used = consumed || change.LiquidChanged;
            string kind = change.SourceBefore.Kind ?? "";
            string message = used ? Message(change, kind, consumed) : NotUsed(change);
            return new HandsResult
            {
                Success = used,
                Code = used ? "ok" : "not_used",
                Message = message,
                Data = new
                {
                    effect = kind,
                    source = new
                    {
                        item_id = change.SourceBefore.Item.Id,
                        name = change.SourceBefore.Item.Name,
                        quantity_before = change.SourceBefore.Quantity,
                        quantity_after = change.SourceAfter?.Quantity,
                        consumed
                    },
                    target = new
                    {
                        item_id = change.TargetAfter.Item.Id,
                        name = change.TargetAfter.Item.Name,
                        path = change.TargetAfter.Item.Path,
                        volume_before_ml = Ml(change.TargetBefore.VolumeUl),
                        volume_ml = Ml(change.TargetAfter.VolumeUl),
                        capacity_ml = Ml(change.TargetAfter.CapacityUl)
                    }
                }
            };
        }

        private static string Message(ItemUseChange change, string kind, bool consumed)
        {
            string target = change.TargetAfter.Item.Name;
            string source = change.SourceBefore.Item.Name;
            double volume = Ml(change.TargetAfter.VolumeUl);
            double capacity = Ml(change.TargetAfter.CapacityUl);
            switch (kind)
            {
                case ItemUseRules.WaterRation:
                    return F("已把 {0} 加入 {1}；水配给{2}。容器现有 {3}/{4} ml。",
                        source, target, consumed ? T("已被原生行为消耗") : T("仍保留"), volume, capacity);
                case ItemUseRules.UVFilter:
                    return F("已对 {0} 使用 {1}；UV 过滤器{2}。容器现有 {3}/{4} ml。水质请用试纸、扫描或 analyze_water 确认。",
                        target, source, consumed ? T("已被原生行为消耗") : T("仍保留"), volume, capacity);
                case ItemUseRules.NutrientTablet:
                    return F("已对 {0} 使用 {1}；营养药片{2}。容器现有 {3}/{4} ml。原生反应可能需要时间，水质 / 养分请用试纸、扫描或 analyze_water 确认。",
                        target, source, consumed ? T("已被原生行为消耗") : T("仍保留"), volume, capacity);
                default:
                    return F("已对 {0} 使用 {1}；净水药片{2}。容器现有 {3}/{4} ml。净化反应可能需要时间，水质请用试纸、扫描或 analyze_water 确认。",
                        target, source, consumed ? T("已被原生行为消耗") : T("仍保留"), volume, capacity);
            }
        }

        private static string NotUsed(ItemUseChange change)
            => F("没有发生物品使用：{0} 与 {1} 的当前状态不满足原生使用条件。",
                change.SourceBefore.Item.Name, change.TargetAfter.Item.Name);

        private static double Ml(int microliters) => Math.Round(microliters / 1000d, 2);

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
