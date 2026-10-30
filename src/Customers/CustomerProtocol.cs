using Newtonsoft.Json.Linq;

namespace ProbablyAI.Customers
{
    public static class CustomerProtocol
    {
        public static JArray Tools() => new JArray
        {
            Tool("get_current_customer", "读取当前已到达客人的身份、来店意图、剩余购买预算、购买品类/指定物品/拒收规则、当前显示的对话及易货/采购/枪械委托需求。只读。BUY 是客人买店主的货，SELL 是客人卖货给店主。没有客人或正在到达时不返回旧客人；每次讨论当前客人先重新查询。预算未启用不等于无限现金。",
                new JObject(), new JArray()),
            Tool("check_customer_item", "使用游戏原生规则检查一个真实物品是否符合当前客人的普通购买偏好及当前预算，并列出匹配的易货条件索引。只读，不移动或成交，不保证特殊委托已完成；价格是当前物品估值，放上柜台或谈价后可能变化。需要刚查询的客人 ID 和物品 ID，客人改变时会拒绝旧 ID。",
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
