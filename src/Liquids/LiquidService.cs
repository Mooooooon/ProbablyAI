using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Liquids
{
    // Shared validation and result mapping for liquid tools. The native backend performs the
    // actual item-on-item action; this layer never moves liquid by itself.
    public sealed class LiquidService
    {
        private readonly ILiquidBackend backend;
        public LiquidService(ILiquidBackend backend) => this.backend = backend;

        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in LiquidProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => Execute(name, arguments), changesState: name != "inspect_liquid");
            }
        }

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                switch (tool)
                {
                    case "inspect_liquid":
                        ValidateKeys(arguments, "item_id");
                        var reading = backend.Inspect(Id(arguments, "item_id"));
                        var error = LiquidRules.ValidateContainer(reading);
                        return error == null ? HandsResult.Ok(Describe(reading)) : HandsResult.Fail(error.Code, error.Message);
                    case "pour_liquid":
                        ValidateKeys(arguments, "source_item_id", "target_item_id", "require_full_transfer");
                        var sourceId = Id(arguments, "source_item_id");
                        var targetId = Id(arguments, "target_item_id");
                        if (sourceId == targetId) throw new HandsException("same_item", T("不能把容器倒进它自己。"));
                        var strict = arguments["require_full_transfer"];
                        if (strict != null && strict.Type != JTokenType.Boolean)
                            throw new HandsException("invalid_arguments", T("require_full_transfer 必须为布尔值。"));
                        bool requireFullTransfer = (bool?)strict ?? false;
                        return Pour(backend.Pour(sourceId, targetId, requireFullTransfer), requireFullTransfer);
                    case "dump_liquid":
                        ValidateKeys(arguments, "item_id");
                        return Dump(backend.Dump(Id(arguments, "item_id")));
                    case "fill_liquid":
                        ValidateKeys(arguments, "item_id");
                        return Fill(backend.Fill(Id(arguments, "item_id")));
                    default:
                        return HandsResult.Fail("unknown_tool", T("未知的液体工具。"));
                }
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("liquid_failed", T("液体操作未完成，请刷新容器后重试。不要重复执行未核实的操作。")); }
        }

        private static HandsResult Pour(LiquidChange change, bool requireFullTransfer)
        {
            RequireChange(change, hasTarget: true);
            int available = change.Direction == LiquidChange.TargetToSource
                ? change.TargetBefore.VolumeUl : change.SourceBefore.VolumeUl;
            RequireMoved(change.MovedUl, available);
            bool moved = change.MovedUl > 0;
            bool partial = requireFullTransfer && moved &&
                (change.Direction != LiquidChange.SourceToTarget || change.MovedUl != change.SourceBefore.VolumeUl || !change.SourceAfter.Empty);
            return new HandsResult
            {
                Success = moved && !partial,
                Code = partial ? "partial" : moved ? "ok" : "not_poured",
                Message = partial ? T("未能核实全部倒出。") + " " + PourMessage(change) : PourMessage(change),
                Data = new
                {
                    source = Describe(change.SourceAfter),
                    target = Describe(change.TargetAfter),
                    direction = change.Direction,
                    moved_ml = Ml(change.MovedUl),
                    received_ml = Ml(change.ReceivedUl),
                    moved_ul = change.MovedUl,
                    received_ul = change.ReceivedUl,
                    filtered_ul = change.FilteredUl,
                    filtered_ml = change.FilteredUl > 0 ? (double?)Ml(change.FilteredUl) : null
                }
            };
        }

        private static HandsResult Dump(LiquidChange change)
        {
            RequireChange(change, hasTarget: false);
            RequireMoved(change.MovedUl, change.SourceBefore.VolumeUl);
            bool emptied = change.MovedUl > 0;
            string message = emptied
                ? F("已把 {0} 中的 {1} ml 液体倒入排水口；容器剩余 {2}/{3} ml。",
                    change.SourceAfter.Item.Name, Ml(change.MovedUl),
                    Ml(change.SourceAfter.VolumeUl), Ml(change.SourceAfter.CapacityUl))
                : F("没有液体倒出：{0} 里没有可倒掉的液体。", change.SourceAfter.Item.Name);
            return new HandsResult
            {
                Success = emptied,
                Code = emptied ? "ok" : "not_dumped",
                Message = message,
                Data = new
                {
                    item = Describe(change.SourceAfter),
                    emptied_ml = Ml(change.MovedUl),
                    remaining_ml = Ml(change.SourceAfter.VolumeUl)
                }
            };
        }

        private static HandsResult Fill(LiquidChange change)
        {
            RequireChange(change, hasTarget: false);
            RequireMoved(change.MovedUl, change.SourceBefore.FreeUl);
            bool filled = change.MovedUl > 0;
            string message = filled
                ? F("已从水龙头向 {0} 加入 {1} ml 水；现有 {2}/{3} ml。",
                    change.SourceAfter.Item.Name, Ml(change.MovedUl),
                    Ml(change.SourceAfter.VolumeUl), Ml(change.SourceAfter.CapacityUl))
                : F("没有接到水：{0} 已满或水龙头当前不可用。", change.SourceAfter.Item.Name);
            return new HandsResult
            {
                Success = filled,
                Code = filled ? "ok" : "not_filled",
                Message = message,
                Data = new
                {
                    item = Describe(change.SourceAfter),
                    added_ml = Ml(change.MovedUl),
                    volume_ml = Ml(change.SourceAfter.VolumeUl),
                    capacity_ml = Ml(change.SourceAfter.CapacityUl)
                }
            };
        }

        private static string PourMessage(LiquidChange change)
        {
            var source = change.SourceAfter;
            var target = change.TargetAfter;
            if (change.MovedUl <= 0)
                return F("没有液体移动：{0} 与 {1} 的状态没有变化。请检查容器是否为空、已满或不允许倒取。",
                    source.Item.Name, target.Item.Name);
            if (change.Direction == LiquidChange.TargetToSource)
                return F("{0} 从 {1} 吸取了 {2} ml 液体；{1} 剩余 {3}/{4} ml，{0} 现有 {5}/{6} ml。",
                    source.Item.Name, target.Item.Name, Ml(change.MovedUl),
                    Ml(target.VolumeUl), Ml(target.CapacityUl), Ml(source.VolumeUl), Ml(source.CapacityUl));
            if (change.FilteredUl > 0)
                return F("已从 {0} 向 {1} 倒出 {2} ml 液体，目标接收 {3} ml（目标过滤器截留了部分杂质）；{0} 剩余 {4}/{5} ml，{1} 现有 {6}/{7} ml。",
                    source.Item.Name, target.Item.Name, Ml(change.MovedUl), Ml(change.ReceivedUl),
                    Ml(source.VolumeUl), Ml(source.CapacityUl), Ml(target.VolumeUl), Ml(target.CapacityUl));
            return F("已从 {0} 向 {1} 倒出 {2} ml 液体；{0} 剩余 {3}/{4} ml，{1} 现有 {5}/{6} ml。",
                source.Item.Name, target.Item.Name, Ml(change.MovedUl),
                Ml(source.VolumeUl), Ml(source.CapacityUl), Ml(target.VolumeUl), Ml(target.CapacityUl));
        }

        private static object Describe(LiquidReading reading) => new
        {
            item_id = reading.Item.Id,
            name = reading.Item.Name,
            path = reading.Item.Path,
            volume_ml = Ml(reading.VolumeUl),
            capacity_ml = Ml(reading.CapacityUl),
            free_ml = Ml(reading.FreeUl),
            volume_ul = reading.VolumeUl,
            capacity_ul = reading.CapacityUl,
            free_ul = reading.FreeUl
        };

        private static void RequireChange(LiquidChange change, bool hasTarget)
        {
            if (change?.SourceBefore == null || change.SourceAfter == null ||
                hasTarget && (change.TargetBefore == null || change.TargetAfter == null))
                throw new HandsException("liquid_failed", T("液体操作未完成，请刷新容器后重试。不要重复执行未核实的操作。"));
        }

        private static void RequireMoved(int moved, int available)
        {
            if (moved < 0 || moved > available)
                throw new HandsException("invalid_native_result", T("原生接口返回了异常数量，请刷新物品列表核实。"));
        }

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
                throw new HandsException("invalid_arguments", T("液体操作需要先前查询返回的有效物品 ID，且不接受其他参数。"));
            return ((string)value).Trim();
        }
    }
}
