using Newtonsoft.Json.Linq;

namespace ProbablyAI.Inspection
{
    public static class InspectionProtocol
    {
        public static JArray Tools() => new JArray
        {
            Tool("appraise_mechanical_hand", "只读鉴定真实机械手，返回材质、控制接口、接口标记、磨损、装甲/刀刃附加件、套装及游戏原生单件鉴定基准价 base_value。该价格已含套装效果，不再叠加；不是交易报价或鉴定服务报酬。不提交估价、不改变物品或完成委托。需先查询真实物品 ID；鉴定玩法未解锁时返回失败。"),
            Tool("inspect_cigarette", "只读检查真实香烟的封条、标志、文字和包装外观，返回 visual_assessment（appears_genuine/appears_counterfeit/unknown）及可见异常 issues（seal/logo/lettering/color）。0 为标准外观，-1 为缺失，其他编号为外观变体。仅根据检查界面可见的外观判断，不读取隐藏真伪或自动揭露特性，不质问客人、不改价。不确定时如实说明。需先查询真实物品 ID；目视检查未解锁时返回失败。"),
            Tool("inspect_food_stamp", "只读检查真实食品券的防伪标、商标、芯片、文字和票面面值，返回 visual_assessment（appears_genuine/appears_counterfeit/unknown）、可见异常 issues（seal/logo/chip/lettering/amount）及 face_value 信用点面值。0 为标准外观，-1 为缺失，其他编号为外观变体。食品券仅发行 40/80/120/160/200/240 信用点面值，面值达到 160 才应有防伪标；芯片缺失或损坏时无法兑换。仅根据检查界面可见外观判断，不读取隐藏真伪或异常标签，不质问客人、不兑换、不改价。需先查询真实物品 ID；目视检查未解锁时返回失败。")
        };

        private static JObject Tool(string name, string description) => new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = name, ["description"] = description,
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["item_id"] = new JObject { ["type"] = "string", ["description"] = "find_items / list_container 返回的真实物品 ID。" }
                    },
                    ["required"] = new JArray("item_id"), ["additionalProperties"] = false
                }
            }
        };
    }
}
