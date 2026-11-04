using Newtonsoft.Json.Linq;

namespace ProbablyAI.Liquids
{
    public static class LiquidProtocol
    {
        internal const string InspectDescription = "读取自有液体容器的当前液量、容量和余量。ul 字段使用精确微升，不读取隐藏成分。";
        internal const string FullTransferDescription = "可选。true 要求目标在倒水前有足够空间，倒水后核对源已倒空。过滤截留计入源减少量，实收量单独报告。";
        internal const string PourDescription =
            "把真实液体容器里的液体倒入另一个液体容器（水、酒等），使用游戏原生的物品对物品操作，遵守所有权、锁定、容量、过滤器和温度规则。一次倒出源容器能倒入的全部液体，不能指定毫升数；目标有过滤器时可能截留部分杂质。空滴管对容器使用时会改为吸取液体，direction 会标明 source_to_target / target_to_source。返回实际移动、接收的毫升数及两边余量；moved_ml=0 表示没有液体移动。不生成液体、不销毁容器、不改变物品特性、所有权或价格。需先查询两件真实物品 ID。";

        internal const string DumpDescription =
            "把真实液体容器里的液体倒进当铺排水口，与游戏内把容器拖到排水口相同：清空液体但保留容器本身，不销毁物品、不改变物品特性、所有权或价格。返回倒掉的毫升数和容器剩余量。需先查询真实物品 ID。";

        internal const string FillDescription =
            "把真实液体容器拿到当铺水龙头接水，与游戏内把容器拖到水龙头相同：按已解锁的锈水 / 下水道水龙头规则注水，受容器容量、所有权和原生规则限制；一次接满可用空间，不能指定水质或毫升数。水龙头尚未解锁时返回 tap_locked。返回实际加入的毫升数和容器余量；不凭空生成容器、不改变物品特性、所有权或价格。需先查询真实物品 ID。";

        public static JArray Tools() => new JArray
        {
            Tool("inspect_liquid", InspectDescription, new JObject
            {
                ["item_id"] = Text("先查询获得的液体容器物品 ID；不能使用物品名称代替。")
            }, "item_id"),
            Tool("pour_liquid", PourDescription, new JObject
            {
                ["source_item_id"] = Text("先查询获得的源液体容器物品 ID；不能使用物品名称代替。"),
                ["target_item_id"] = Text("先查询获得的目标液体容器物品 ID；不能与源相同。"),
                ["require_full_transfer"] = new JObject { ["type"] = "boolean", ["description"] = FullTransferDescription }
            }, "source_item_id", "target_item_id"),
            Tool("dump_liquid", DumpDescription, new JObject
            {
                ["item_id"] = Text("先查询获得的液体容器物品 ID；不能使用物品名称代替。")
            }, "item_id"),
            Tool("fill_liquid", FillDescription, new JObject
            {
                ["item_id"] = Text("先查询获得的液体容器物品 ID；不能使用物品名称代替。")
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
