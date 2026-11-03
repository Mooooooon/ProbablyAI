using Newtonsoft.Json.Linq;

namespace ProbablyAI.Labels
{
    public static class LabelProtocol
    {
        public const int MaxNameLength = 200;
        public static JArray Tools() => new JArray
        {
            Tool("get_label_options", "查询真实标签机对目标物品可编辑的标签类别和选项，或蓝色标签机的当前自定义名称与长度限制。需先找到标签机和目标物品 ID；检查原生所有权、锁定和使用规则。返回 category_id、condition_id 与可见文字，不读取隐藏真实属性，不打开窗口或修改物品。改标签前先查询，不猜测选项 ID。",
                Fields(), "tool_item_id", "target_item_id"),
            Tool("set_item_label", "使用普通标签打印机 labeler 修改目标的一项标签。category_id 和 condition_id 必须来自 get_label_options；按原生标签窗口执行选择并重新读取结果。只改标签，不改变真实属性或水质；可能清除原生揭露标记，报价变化由游戏决定。不代办交易。需真实可用的标签机，遵守所有权、锁定和场景规则。",
                Fields("category_id", "condition_id"), "tool_item_id", "target_item_id", "category_id", "condition_id"),
            Tool("rename_item", "使用蓝色标签机 labeler_blue 修改目标的自定义名称。name 为用户要求的文字，空字符串清除自定义名称并恢复原名；最多 200 字且遵守原生输入框更短的长度限制，不接受换行或控制字符。通过原生改名窗口执行并核对结果。不会修改物品标识、真实属性或所有权，不能重命名档案架 dossier。",
                RenameFields(), "tool_item_id", "target_item_id", "name")
        };

        private static JObject Fields(params string[] extra)
        {
            var fields = new JObject
            {
                ["tool_item_id"] = Id("先查询获得的真实标签机物品 ID。"),
                ["target_item_id"] = Id("先查询获得的目标物品 ID。")
            };
            foreach (string field in extra) fields[field] = Id("get_label_options 返回的选项 ID，不使用显示文字代替。");
            return fields;
        }
        private static JObject RenameFields()
        {
            var fields = Fields();
            fields["name"] = new JObject { ["type"] = "string", ["maxLength"] = MaxNameLength,
                ["description"] = "新的自定义名称；空字符串清除名称，不能包含换行或控制字符。" };
            return fields;
        }
        private static JObject Id(string description) => new JObject
        { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200, ["description"] = description };
        private static JObject Tool(string name, string description, JObject fields, params string[] required) => new JObject
        {
            ["type"] = "function", ["function"] = new JObject { ["name"] = name, ["description"] = description,
                ["parameters"] = new JObject { ["type"] = "object", ["properties"] = fields,
                    ["required"] = new JArray(required), ["additionalProperties"] = false } }
        };
    }
}
