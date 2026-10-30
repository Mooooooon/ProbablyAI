using Newtonsoft.Json.Linq;

namespace ProbablyAI.Customers
{
    public static class CustomerProtocol
    {
        public static JArray Tools() => new JArray
        {
            Tool("get_current_customer", "读取当前已到达客人的身份、来店意图、剩余购买预算、购买品类/指定物品/拒收规则、当前显示的对话及易货/采购/枪械委托需求。只读。BUY 是客人买店主的货，SELL 是客人卖货给店主。没有客人或正在到达时不返回旧客人；每次讨论当前客人先重新查询。预算未启用不等于无限现金。",
                new JObject(), new JArray()),
            Tool("check_customer_item", "只读检查真实物品的普通购买偏好、含乘区的售价及剩余预算，并列出匹配的易货条件索引。current_item_value 已含 pricing 中的所有效果，不要再次加价；属性区与市场区分别相加，最终谈价区逐项相乘。柜台顶层读原生报价，其余自有物品预估上柜台后的多种多样、实时价格事件、零售加价及已知客人效果。上柜台、检查或谈价后重新查询；多件须合计报价，不能分别预算内就认为合计够。需要新查询的客人 ID 和物品 ID；不移动、成交或保证完成委托。",
                new JObject
                {
                    ["customer_id"] = new JObject { ["type"] = "string", ["description"] = "get_current_customer 返回的本次到访 ID。" },
                    ["item_id"] = new JObject { ["type"] = "string", ["description"] = "find_items / list_container 返回的真实物品 ID。" }
                }, new JArray("customer_id", "item_id"))
        };

        private static JObject Tool(string name, string description, JObject properties, JArray required) => new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = name, ["description"] = description,
                ["parameters"] = new JObject
                {
                    ["type"] = "object", ["properties"] = properties,
                    ["required"] = required, ["additionalProperties"] = false
                }
            }
        };
    }
}
