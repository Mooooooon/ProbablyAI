using Newtonsoft.Json.Linq;

namespace ProbablyAI.Items
{
    public static class ItemUseProtocol
    {
        internal const string UseDescription =
            "对真实液体容器使用安全的净水 / 施肥 / 过滤消耗品。仅支持净水药片（water_tablet / WATER_PURIFICATION_TABLET）、营养药片（nutrient_tablet / NUTRIENT_TABLET）、UV 过滤器（uv_filter）和水配给（water_ration / water_ration_small）。使用游戏原生的物品对物品操作，由游戏判定所有权、锁定、目标容器、容量和消耗；药片与水配给通常会被原生行为消耗，UV 过滤器不会被消耗。目标必须是水容器；药片和 UV 过滤器要求容器里已有水，水配给也可以倒入空容器。酒类或化学产品容器会被拒绝。不会打开界面、不读取或公布未测试的水质纯度；净化反应可能需要时间，水质请用试纸 / 扫描或 analyze_water 确认。不支持武器、医疗、手术、动物、机器启动或其他物品。需先查询两件真实物品 ID。";

        public static JArray Tools() => new JArray
        {
            Tool("use_item_on_item", UseDescription, new JObject
            {
                ["item_id"] = Text("要使用的安全消耗品 / 工具物品 ID；先用 find_items / list_container 查询。"),
                ["target_item_id"] = Text("要作用的液体容器物品 ID；不能与源物品相同。")
            }, "item_id", "target_item_id")
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
