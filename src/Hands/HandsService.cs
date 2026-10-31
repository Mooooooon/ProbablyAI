using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Hands
{
    // All callers (manual UI and model tools) share this validation and operation layer.
    public sealed class HandsService
    {
        private readonly IHandsBackend backend;
        private readonly Queue<string> history = new Queue<string>();
        public IReadOnlyList<string> History => history.ToArray();
        public HandsService(IHandsBackend backend) => this.backend = backend;
        public WorldSnapshot Snapshot() => backend.Capture();

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                if (arguments == null) throw new HandsException("invalid_arguments", T("参数必须是 JSON 对象。"));
                var world = backend.Capture(); // Never trust an earlier model/UI snapshot before a move.
                switch (tool)
                {
                    case "list_containers":
                        ValidateKeys(arguments, "query", "parent_id", "offset", "limit");
                        var query = Text(arguments, "query", false);
                        var parent = Text(arguments, "parent_id", false);
                        if (!string.IsNullOrEmpty(parent)) Container(world, parent);
                        var containers = world.Containers.Where(c => (string.IsNullOrEmpty(parent) || c.ParentId == parent) &&
                            (string.IsNullOrEmpty(query) || Matches(c.Name, query) || Matches(c.Path, query) || Matches(c.Role, query))).ToList();
                        return Page(containers, arguments, world.Truncated);
                    case "list_container":
                        ValidateKeys(arguments, "container_id", "offset", "limit");
                        var container = Container(world, Text(arguments, "container_id"));
                        if (container.Locked) throw new HandsException("container_locked", T("容器已锁定，无法查看内容。"));
                        return HandsResult.Ok(new
                        {
                            container,
                            children = world.Containers.Where(c => c.ParentId == container.Id).ToArray(),
                            items = PageData(world.Items.Where(i => i.ContainerId == container.Id).ToList(), arguments),
                            world.Truncated
                        });
                    case "find_items":
                        ValidateKeys(arguments, "query", "item_type", "owned_only", "container_id", "include_in_use", "offset", "limit");
                        var search = Text(arguments, "query", false);
                        var itemType = Text(arguments, "item_type", false);
                        bool ownedOnly = Flag(arguments, "owned_only");
                        bool includeInUse = Flag(arguments, "include_in_use");
                        if ((string.IsNullOrWhiteSpace(itemType) || string.Equals(itemType, "FOOD", StringComparison.OrdinalIgnoreCase)) &&
                            (search == "食物" || search == "食品" || string.Equals(search, "food", StringComparison.OrdinalIgnoreCase)))
                        { itemType = "FOOD"; search = null; }
                        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(itemType))
                            throw new HandsException("invalid_arguments", T("请提供搜索词 query 或物品类型 item_type。"));
                        var scope = Text(arguments, "container_id", false);
                        if (!string.IsNullOrEmpty(scope)) Container(world, scope);
                        var items = world.Items.Where(i =>
                            (string.IsNullOrEmpty(scope) || Inside(world, i.ContainerId, scope)) &&
                            (includeInUse || i.InUse != true) &&
                            (!ownedOnly || i.Owned) &&
                            (string.IsNullOrEmpty(itemType) || i.Types.Any(t => string.Equals(t, itemType, StringComparison.OrdinalIgnoreCase))) &&
                            (string.IsNullOrEmpty(search) || Matches(i.Name, search) || Matches(i.Identifier, search) || Matches(i.Path, search))).ToList();
                        return Page(items, arguments, world.Truncated);
                    case "inspect_item":
                        ValidateKeys(arguments, "item_id");
                        return HandsResult.Ok(Item(world, Text(arguments, "item_id")));
                    case "can_move_item":
                    case "move_item":
                        ValidateKeys(arguments, "item_id", "source_container_id", "destination_container_id", "quantity");
                        return Transfer(world, arguments, tool == "can_move_item");
                    case "open_container":
                    case "close_container":
                        ValidateKeys(arguments, "container_id");
                        var target = Container(world, Text(arguments, "container_id"));
                        if (target.Locked) throw new HandsException("container_locked", T("容器已锁定，无法打开。"));
                        backend.SetOpen(target.Id, tool == "open_container");
                        return HandsResult.Ok(new { container_id = target.Id }, tool == "open_container" ? T("已打开容器窗口。") : T("已关闭容器窗口。"));
                    default: return HandsResult.Fail("unknown_tool", T("不支持的操作接口。"));
                }
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("game_error", T("游戏操作未完成，请刷新列表检查当前物品位置。不要重复执行未核实的操作。")); }
        }

        private HandsResult Transfer(WorldSnapshot world, JObject arguments, bool preview)
        {
            var item = Item(world, Text(arguments, "item_id"));
            var source = Container(world, Text(arguments, "source_container_id"));
            var destination = Container(world, Text(arguments, "destination_container_id"));
            if (arguments["quantity"] == null) throw new HandsException("invalid_arguments", T("缺少参数：quantity（0 代表所选这一堆全部）。"));
            var quantity = Number(arguments, "quantity", 0, 0, int.MaxValue);
            if (item.ContainerId != source.Id)
                throw new HandsException("source_changed", T("物品位置已经变化，请重新查询后操作。"));
            if (source.Id == destination.Id)
                throw new HandsException("same_container", T("物品已在目标容器中。"));
            if (source.Locked || source.RemoveLocked || destination.Locked || destination.InsertLocked)
                throw new HandsException("container_locked", T("源容器禁止取出，或目标容器禁止放入。"));
            if (!item.Removable) throw new HandsException("item_locked", T("游戏当前不允许移动这个物品。"));
            if (destination.Kind == "group")
                throw new HandsException("choose_compartment", T("目标含多个分区，请先 list_container，再选择具体的原料、成品或储藏分页。"));
            if (!string.IsNullOrEmpty(item.ContentContainerId) && Inside(world, destination.Id, item.ContentContainerId))
                throw new HandsException("container_cycle", T("不能将容器放进自身或自身的子容器。"));
            int requested = quantity == 0 ? item.Quantity : quantity;
            if (requested < 1 || requested > item.Quantity)
                throw new HandsException("invalid_quantity", T("数量不足，或物品已不存在，请刷新列表。"));
            var check = backend.CheckMove(item.Id, destination.Id, requested);
            if (!check.Allowed) throw new HandsException("move_rejected", check.Reason);
            if (preview) return HandsResult.Ok(new
            {
                item_id = item.Id,
                source_container_id = source.Id,
                destination_container_id = destination.Id,
                requested,
                first_slot_quantity = check.FirstSlotQuantity,
                note = T("仅预检，不移动物品；执行时重新检查，空间不足时可能只移动部分。")
            }, T("可以尝试移动。"));

            var outcome = backend.Move(item.Id, destination.Id, requested);
            if (outcome.Moved < 0 || outcome.Moved > requested)
                throw new HandsException("invalid_native_result", T("原生接口返回了异常数量，请刷新物品列表核实。"));
            string message = F("{0}：{1} → {2}，已移动 {3}/{4}。", item.Name, source.Path, destination.Path, outcome.Moved, requested);
            if (!string.IsNullOrWhiteSpace(outcome.Detail)) message += " " + outcome.Detail;
            history.Enqueue(message);
            while (history.Count > 50) history.Dequeue();
            return new HandsResult
            {
                Success = outcome.Moved == requested,
                Code = outcome.Moved == requested ? "ok" : outcome.Moved > 0 ? "partial" : "not_moved",
                Message = message,
                Data = new { item_id = item.Id, source_container_id = source.Id, destination_container_id = destination.Id,
                    requested, moved = outcome.Moved, remaining = requested - outcome.Moved }
            };
        }

        private static object PageData<T>(List<T> values, JObject arguments)
        {
            int offset = Number(arguments, "offset", 0, 0, int.MaxValue);
            int limit = Number(arguments, "limit", 40, 1, 100);
            return new { total = values.Count, offset, has_more = offset < values.Count && values.Count - offset > limit,
                entries = values.Skip(offset).Take(limit).ToArray() };
        }

        private static HandsResult Page<T>(List<T> values, JObject arguments, bool truncated)
            => HandsResult.Ok(new { page = PageData(values, arguments), truncated });

        private static bool Matches(string value, string query)
            => value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private static ContainerInfo Container(WorldSnapshot world, string id)
            => world.Containers.FirstOrDefault(c => c.Id == id) ?? throw new HandsException("container_not_found", T("容器 ID 已失效或不可访问，请重新获取容器列表。"));
        private static ItemInfo Item(WorldSnapshot world, string id)
            => world.Items.FirstOrDefault(i => i.Id == id) ?? throw new HandsException("item_not_found", T("物品 ID 已失效或不可访问，请重新搜索物品。"));

        private static bool Inside(WorldSnapshot world, string child, string ancestor)
        {
            var visited = new HashSet<string>();
            while (!string.IsNullOrEmpty(child) && visited.Add(child))
            {
                if (child == ancestor) return true;
                child = world.Containers.FirstOrDefault(c => c.Id == child)?.ParentId;
            }
            return false;
        }

        private static void ValidateKeys(JObject arguments, params string[] keys)
        {
            if (arguments.Properties().Any(p => !keys.Contains(p.Name)))
                throw new HandsException("invalid_arguments", T("包含不支持的参数。"));
        }

        private static string Text(JObject arguments, string name, bool required = true)
        {
            var value = arguments[name];
            if (value == null || value.Type == JTokenType.Null)
            {
                if (!required) return null;
                throw new HandsException("invalid_arguments", T("缺少参数：") + name);
            }
            if (value.Type != JTokenType.String || ((string)value).Length > 2048 || required && string.IsNullOrWhiteSpace((string)value))
                throw new HandsException("invalid_arguments", T("参数无效：") + name);
            return ((string)value).Trim();
        }

        private static int Number(JObject arguments, string name, int fallback, int min, int max)
        {
            var value = arguments[name];
            if (value == null) return fallback;
            if (value.Type != JTokenType.Integer || !long.TryParse(value.ToString(), out var number) || number < min || number > max)
                throw new HandsException("invalid_arguments", T("数值参数无效：") + name);
            return (int)number;
        }

        private static bool Flag(JObject arguments, string name)
        {
            if (arguments[name] == null) return false;
            if (arguments[name].Type != JTokenType.Boolean) throw new HandsException("invalid_arguments", T("布尔参数无效：") + name);
            return (bool)arguments[name];
        }
    }
}
