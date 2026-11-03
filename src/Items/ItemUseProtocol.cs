using Newtonsoft.Json.Linq;

namespace ProbablyAI.Items
{
    public static class ItemUseProtocol
    {
        internal const string UseDescription =
            "对另一个真实物品或角色使用真实物品，复刻玩家在游戏内的物品对物品操作。任何非战斗物品都可以尝试，是否允许、消耗和效果完全由游戏原生 MayTarget / CanTarget / Target 判定；助手不设白名单、不绕过所有权、锁定、容量或规则。武器、弹药、爆炸物、战斗能力和敌人目标会被拒绝，战斗中的操作也会被拒绝。试纸、扫描仪、书籍、医疗、手术、动物、机器和工具等非战斗物品可以使用；其中一些会打开游戏原生窗口，请按原生提示继续。结果报告源物品是否消耗、是否观察到状态变化，以及目标液量（如适用）。不代替玩家选择对话、买卖、提交报价或改存档。需先查询两件真实物品 ID。";

        internal const string ActivateDescription =
            "使用或激活一个真实物品，相当于玩家在物品栏里双击 / 使用它。走游戏原生 MayActivateSlotItem / CanActivateSlotItem / ActivateSlotItem；食物、饮料、书籍、试纸、扫描仪、医疗用品、工具等非战斗物品都可以尝试。武器、弹药、爆炸物和战斗能力会被拒绝，战斗中的操作也会被拒绝。原生行为可能消耗物品、打开窗口或触发对话；助手只报告实际消耗或观察到的状态变化，不代替玩家选择对话、买卖或改存档。需先查询物品 ID。";

        public static JArray Tools() => new JArray
        {
            Tool("use_item_on_item", UseDescription, new JObject
            {
                ["item_id"] = Text("要使用的真实物品 ID；先用 find_items / list_container 查询。"),
                ["target_item_id"] = Text("要作用的真实物品或角色 ID；不能与源物品相同。")
            }, "item_id", "target_item_id"),
            Tool("activate_item", ActivateDescription, new JObject
            {
                ["item_id"] = Text("要使用 / 激活的真实物品 ID；先用 find_items / list_container 查询。")
            }, "item_id")
        };

        private static JObject Text(string description) => new JObject { ["type"] = "string", ["description"] = description };
        private static JObject Tool(string name, string description, JObject properties, params string[] required)
            => new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = name, ["description"] = description,
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object", ["properties"] = properties.DeepClone(),
                        ["required"] = new JArray(required), ["additionalProperties"] = false
                    }
                }
            };
    }
}
