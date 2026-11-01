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
            var batch = new JObject
            {
                ["destination_container_id"] = Text("所有移动共用的目标容器 ID。机器必须指定原料、成品、电池、模块等具体分区。"),
                ["moves"] = new JObject
                {
                    ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 20,
                    ["description"] = "要按顺序移动的物品堆，每项写明物品 ID 和当前所在容器。",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["item_id"] = Text("先查询获得的具体物品 ID；不能使用物品名称代替。"),
                            ["source_container_id"] = Text("查询中物品当前所在容器 ID，用于检测位置变化。"),
                            ["quantity"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["description"] = "可选，移动数量；0 或省略代表这个物品堆的全部数量，不能代表全部同名物品。" }
                        },
                        ["required"] = new JArray("item_id", "source_container_id"),
                        ["additionalProperties"] = false
                    }
                }
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
            search["include_in_use"] = new JObject { ["type"] = "boolean", ["description"] = "默认 false，隐藏 in_use=true 的设备供电或充电电池，以及冰箱中尚未冻好或冰镇好的物品；主人明确要求查看或更换时才设为 true。" };
            search["include_pinned"] = new JObject { ["type"] = "boolean", ["description"] = "默认 false，隐藏 pinned=true 的固定物品本身，以及固定非储藏、非机器容器（如试剂盒、净水药片盒）或固定机器原料/模组/电池区中的物品；固定储藏（STORAGE）内物品、固定机器成品区（output）、酒架酒槽（wine）及冰箱中已冻好/冰镇好的物品仍可返回。主人明确要求查看固定物品时才设为 true。" };
            search["include_stolen"] = new JObject { ["type"] = "boolean", ["description"] = "默认 false，隐藏 stolen=true 的赃物；主人明确要求查看或挑选赃物时才设为 true。" };
            return new JArray
            {
                Tool("list_containers", "获取当前当铺的容器树，包括关闭的自有容器、嵌套文件夹和机器各分区。返回 ID、完整路径、父级、角色及锁定状态；不读取锁定容器内容。", list),
                Tool("list_container", "列出一个容器的直接物品和直接子容器。多分区机器需继续查询具体分区。", contents, "container_id"),
                Tool("find_items", "按名称、嵌套路径或游戏物品类型递归查找，返回唯一 ID、数量、类型、位置、赃物标记和热度。找所有食物应设 item_type=FOOD，无需名称含食物。默认不返回在用物品（in_use=true，含供电/充电电池及冰箱内未冻好物品）、受固定限制的物品（pinned=true 的物品本身，以及固定非储藏、非机器容器内或固定机器非售卖分区的物品）以及赃物（stolen=true）。", search),
                Tool("inspect_item", "查看某个真实物品的描述、数量、所有权、位置、内容容器 ID、赃物标记与热度。", new JObject { ["item_id"] = Text("物品 ID。") }, "item_id"),
                Tool("can_move_item", "预检单个物品堆是否能放入目标容器，不移动。执行时可能因容量或状态变化只移动一部分。", move, "item_id", "source_container_id", "destination_container_id", "quantity"),
                Tool("move_item", "按游戏原生规则移动一个物品堆，支持上柜台、客人窗口到柜台、入库、归档、放入垃圾桶和机器投料。一次只移动一件：挑货或批量上柜台时改用 move_items，一次摆放多件。返回实际 moved 和 remaining；partial 不等于全部完成。不买卖、不改变所有权、不清空垃圾桶。", move, "item_id", "source_container_id", "destination_container_id", "quantity"),
                Tool("move_items", "按游戏原生规则依次移动 1–20 个物品堆，一次调用完成多件摆放；挑货、整理和批量上柜台优先用它，而不是连续调用 move_item。每件独立检查原生所有权、锁定、容量和当前位置，已完成的移动不会回滚。返回每件结果及合计 moved；partial 不等于全部完成。所有移动使用同一个目标容器；不买卖、不改变所有权、不清空垃圾桶。", batch, "destination_container_id", "moves"),
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
