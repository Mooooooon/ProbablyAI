using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Labels
{
    public sealed class LabelService
    {
        private readonly ILabelBackend backend;
        public LabelService(ILabelBackend backend) => this.backend = backend;
        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in LabelProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, args => Execute(name, args), name != "get_label_options");
            }
        }
        public HandsResult Execute(string tool, JObject args)
        {
            try
            {
                if (tool != "get_label_options" && tool != "set_item_label" && tool != "rename_item")
                    return HandsResult.Fail("unknown_tool", T("未知的标签操作工具。"));
                var keys = tool == "get_label_options" ? new[] { "tool_item_id", "target_item_id" } :
                    tool == "rename_item" ? new[] { "tool_item_id", "target_item_id", "name" } :
                    new[] { "tool_item_id", "target_item_id", "category_id", "condition_id" };
                if (args == null || args.Properties().Any(p => !keys.Contains(p.Name))) Invalid();
                string source = Id(args, "tool_item_id"), target = Id(args, "target_item_id");
                if (source == target) Invalid();
                if (tool == "get_label_options")
                {
                    var options = backend.Inspect(source, target);
                    if (options?.Item?.Id != target) throw new InvalidOperationException();
                    return HandsResult.Ok(options, T("已读取可编辑标签与名称信息，未修改物品。"));
                }
                LabelChange change;
                string category = null, condition = null, name = null;
                if (tool == "set_item_label")
                {
                    category = Id(args, "category_id"); condition = Id(args, "condition_id");
                    change = backend.SetLabel(source, target, category, condition);
                }
                else
                {
                    if (args["name"]?.Type != JTokenType.String) Invalid();
                    name = (string)args["name"];
                    if (name.Length > LabelProtocol.MaxNameLength || name.Any(char.IsControl) ||
                        name.Length > 0 && string.IsNullOrWhiteSpace(name)) Invalid();
                    change = backend.Rename(source, target, name);
                }
                if (change?.Before?.Item?.Id != target || change.After?.Item?.Id != target) throw new InvalidOperationException();
                bool verified = tool == "rename_item" ? (change.After.CustomName ?? "") == name :
                    change.After.Labels.Any(l => l.CategoryId == category && l.CurrentConditionId == condition);
                return new HandsResult
                {
                    Success = verified, Code = verified ? "ok" : "not_applied", Data = change,
                    Message = !verified ? T("未能核实标签或名称已按要求修改，请重新查询后再决定是否重试。") :
                        tool == "set_item_label" ? T("标签已按原生选项设置并核实；真实属性和水质没有因此改变。") :
                        name.Length == 0 ? T("已清除自定义名称并核实。") : T("自定义名称已设置并核实。")
                };
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("label_failed", T("标签操作未完成，请重新查询物品与标签状态，不要盲目重复执行。")); }
        }
        private static string Id(JObject args, string key)
        {
            if (args[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args[key]) || ((string)args[key]).Length > 200) Invalid();
            return ((string)args[key]).Trim();
        }
        private static void Invalid() => throw new HandsException("invalid_arguments", T("请提供有效的标签机、目标和选项 ID；名称最多 200 字且不能包含换行或控制字符，不接受多余参数。"));
    }
}
