using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ProbablyAI.Hands
{
    public static class HandsProtocol
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore
        };
        public static string Json(object value) => JsonConvert.SerializeObject(value, Formatting.None, Settings);

        public static JArray Tools()
        {
            var pagination = new JObject
            {
                ["offset"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["description"] = "分页起点，默认 0。" },
                ["limit"] = new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["description"] = "每页条目数，默认 40。" }
            };
            var move = new JObject
            {
                ["item_id"] = Text("先查询获得的具体物品 ID；不能使用物品名称代替。"),
                ["source_container_id"] = Text("查询中物品当前所在容器 ID，用于检测位置变化。"),
                ["destination_container_id"] = Text("目标容器 ID。机器必须指定原料、成品、电池、模块等具体分区。"),
                ["quantity"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["description"] = "移动数量；0 代表这个物品堆的全部数量，不能代表全部同名物品。" }
            };
            var list = (JObject)pagination.DeepClone();
            list["query"] = Text("可选：按容器名、路径或分区角色搜索。空字符串返回所有可访问容器。");
            list["parent_id"] = Text("可选：只列出这个容器的直接子容器。");
            var contents = (JObject)pagination.DeepClone(); contents["container_id"] = Text("要查看的容器 ID。");
            var search = (JObject)pagination.DeepClone();
            search["query"] = Text("可选：按物品名称或完整路径搜索；食物/食品/food 会按 FOOD 类别搜索。query 与 item_type 至少提供一个。");
            search["item_type"] = Text("可选：精确匹配游戏类型，如 FOOD（全部食物，包括名称不含食物的面包、罐头）、MATERIAL、DOCUMENT。忽略大小写。");
            search["owned_only"] = new JObject { ["type"] = "boolean", ["description"] = "true 只找自有物品；默认 false。未指定来源的整理请求应使用 true，并优先限定物品栏。" };
            search["container_id"] = Text("可选：只搜索此容器及其所有子容器。");
            search["include_in_use"] = new JObject { ["type"] = "boolean", ["description"] = "默认 false，隐藏 in_use=true 的设备供电或充电电池；主人明确要求查看或更换电池时才设为 true。" };
            search["include_pinned"] = new JObject { ["type"] = "boolean", ["description"] = "默认 false，隐藏 pinned=true 且本身不是容器的已固定物品；主人明确要求查看这些固定物品时才设为 true。" };
            return new JArray
            {
                Tool("list_containers", "获取当前当铺的容器树，包括关闭的自有容器、嵌套文件夹和机器各分区。返回 ID、完整路径、父级、角色及锁定状态；不读取锁定容器内容。", list),
                Tool("list_container", "列出一个容器的直接物品和直接子容器。多分区机器需继续查询具体分区。", contents, "container_id"),
                Tool("find_items", "按名称、嵌套路径或游戏物品类型递归查找，返回唯一 ID、数量、类型和位置。找所有食物应设 item_type=FOOD，无需名称含食物。默认不返回设备中供电或充电的电池（in_use=true），也不返回已固定且不是容器的物品（pinned=true）。", search),
                Tool("inspect_item", "查看某个真实物品的描述、数量、所有权、位置及内容容器 ID。", new JObject { ["item_id"] = Text("物品 ID。") }, "item_id"),
                Tool("can_move_item", "预检单个物品堆是否能放入目标容器，不移动。执行时可能因容量或状态变化只移动一部分。", move, "item_id", "source_container_id", "destination_container_id", "quantity"),
                Tool("move_item", "按游戏原生规则移动一个物品堆，支持上柜台、客人窗口到柜台、入库、归档、放入垃圾桶和机器投料。返回实际 moved 和 remaining；partial 不等于全部完成。不买卖、不改变所有权、不清空垃圾桶。", move, "item_id", "source_container_id", "destination_container_id", "quantity"),
                Tool("open_container", "显示指定容器的原生窗口，不解锁容器、不启动机器。", new JObject { ["container_id"] = Text("容器 ID。") }, "container_id"),
                Tool("close_container", "关闭指定容器的原生窗口。", new JObject { ["container_id"] = Text("容器 ID。") }, "container_id")
            };
        }

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
