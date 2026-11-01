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
                        ValidateKeys(arguments, "query", "item_type", "owned_only", "container_id", "include_in_use", "include_pinned", "include_stolen", "offset", "limit");
                        var search = Text(arguments, "query", false);
                        var itemType = Text(arguments, "item_type", false);
                        bool ownedOnly = Flag(arguments, "owned_only");
                        bool includeInUse = Flag(arguments, "include_in_use");
                        bool includePinned = Flag(arguments, "include_pinned");
                        bool includeStolen = Flag(arguments, "include_stolen");
                        if ((string.IsNullOrWhiteSpace(itemType) || string.Equals(itemType, "FOOD", StringComparison.OrdinalIgnoreCase)) &&
                            (search == "食物" || search == "食品" || string.Equals(search, "food", StringComparison.OrdinalIgnoreCase)))
                        { itemType = "FOOD"; search = null; }
                        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(itemType))
                            throw new HandsException("invalid_arguments", T("请提供搜索词 query 或物品类型 item_type。"));
                        var scope = Text(arguments, "container_id", false);
                        if (!string.IsNullOrEmpty(scope)) Container(world, scope);
                        var containersById = world.Containers.Where(c => !string.IsNullOrEmpty(c.Id)).ToDictionary(c => c.Id, c => c);
                        var itemsById = world.Items.Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id, i => i);
                        var ownerByContainerId = BuildContainerOwners(world, itemsById);
                        var items = world.Items.Where(i =>
                            (string.IsNullOrEmpty(scope) || Inside(world, i.ContainerId, scope)) &&
                            (includeInUse || i.InUse != true) &&
                            (includePinned || !IsHeldByPin(i, containersById, ownerByContainerId)) &&
                            (includeStolen || i.Stolen != true) &&
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
                    case "move_items":
                        ValidateKeys(arguments, "destination_container_id", "moves");
                        return TransferMany(world, arguments);
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
            var requested = Validated(world, item, source, destination, quantity);
            var check = backend.CheckMove(item.Id, source.Id, destination.Id, requested);
            if (!check.Allowed) throw new HandsException(check.Code ?? "move_rejected", check.Reason);
            if (preview) return HandsResult.Ok(new
            {
                item_id = item.Id,
                source_container_id = source.Id,
                destination_container_id = destination.Id,
                requested,
                first_slot_quantity = check.FirstSlotQuantity,
                note = T("仅预检，不移动物品；执行时重新检查，空间不足时可能只移动部分。")
            }, T("可以尝试移动。"));

            var outcome = backend.Move(item.Id, source.Id, destination.Id, requested);
            if (outcome.Moved < 0 || outcome.Moved > requested)
                throw new HandsException("invalid_native_result", T("原生接口返回了异常数量，请刷新物品列表核实。"));
            string message = F("{0}：{1} → {2}，已移动 {3}/{4}。", item.Name, source.Path, destination.Path, outcome.Moved, requested);
            if (!string.IsNullOrWhiteSpace(outcome.Detail)) message += " " + outcome.Detail;
            Record(message);
            return new HandsResult
            {
                Success = outcome.Moved == requested,
                Code = outcome.Moved == requested ? "ok" : outcome.Moved > 0 ? "partial" : "not_moved",
                Message = message,
                Data = new { item_id = item.Id, source_container_id = source.Id, destination_container_id = destination.Id,
                    requested, moved = outcome.Moved, remaining = requested - outcome.Moved }
            };
        }

        // One call places several stacks, so picking goods costs one model round trip instead of
        // one per item. Every stack still goes through the same native checks and callbacks.
        private HandsResult TransferMany(WorldSnapshot world, JObject arguments)
        {
            var entries = Batch(arguments);
            var destination = Container(world, Text(arguments, "destination_container_id"));
            if (destination.Kind == "group")
                throw new HandsException("choose_compartment", T("目标含多个分区，请先 list_container，再选择具体的原料、成品或储藏分页。"));
            if (destination.Locked || destination.InsertLocked)
                throw new HandsException("container_locked", T("目标容器禁止放入。"));
            var moves = new List<BatchMove>();
            foreach (var entry in entries)
            {
                var move = new BatchMove { ItemId = entry.ItemId, SourceContainerId = entry.SourceContainerId, Requested = entry.Quantity };
                moves.Add(move);
                // A structural error aborts before this point; per-item state errors only skip
                // that stack so the remaining goods still reach the counter.
                try
                {
                    var item = Item(world, entry.ItemId);
                    move.ItemName = item.Name;
                    var source = Container(world, entry.SourceContainerId);
                    move.Requested = Validated(world, item, source, destination, entry.Quantity);
                    var check = backend.CheckMove(item.Id, source.Id, destination.Id, move.Requested);
                    if (!check.Allowed) { move.Code = check.Code ?? "move_rejected"; move.Detail = check.Reason; continue; }
                    var outcome = backend.Move(item.Id, source.Id, destination.Id, move.Requested);
                    if (outcome.Moved < 0 || outcome.Moved > move.Requested)
                    {
                        move.Code = "invalid_native_result";
                        move.Detail = T("原生接口返回了异常数量，请刷新物品列表核实。");
                        continue;
                    }
                    move.Moved = outcome.Moved;
                    move.Code = outcome.Moved == move.Requested ? "ok" : outcome.Moved > 0 ? "partial" : "not_moved";
                    move.Detail = outcome.Detail;
                }
                catch (HandsException error)
                {
                    move.Code = error.Code;
                    move.Detail = error.Message;
                    move.Moved = 0;
                }
            }
            int succeeded = moves.Count(m => m.Code == "ok");
            int failed = moves.Count - succeeded;
            int moved = moves.Sum(m => m.Moved);
            int requested = moves.Sum(m => m.Requested);
            string message = failed == 0
                ? F("已移动 {0} 个物品堆到{1}，合计 {2} 件。", moves.Count, destination.Path, moved)
                : F("批量移动 {0} 个物品堆到{1}：完成 {2} 个，失败 {3} 个，合计移动 {4}/{5} 件。", moves.Count, destination.Path, succeeded, failed, moved, requested);
            var firstFailure = moves.FirstOrDefault(m => m.Code != "ok");
            if (firstFailure != null && !string.IsNullOrWhiteSpace(firstFailure.Detail)) message += " " + firstFailure.Detail;
            Record(message);
            return new HandsResult
            {
                Success = failed == 0,
                Code = failed == 0 ? "ok" : moved > 0 ? "partial" : "not_moved",
                Message = message,
                Data = new
                {
                    destination_container_id = destination.Id,
                    total = moves.Count, succeeded, failed, moved, requested,
                    results = moves.Select(m => new
                    {
                        item_id = m.ItemId, item_name = m.ItemName, source_container_id = m.SourceContainerId,
                        requested = m.Requested, moved = m.Moved, code = m.Code, detail = m.Detail
                    }).ToArray()
                }
            };
        }

        private static int Validated(WorldSnapshot world, ItemInfo item, ContainerInfo source, ContainerInfo destination, int quantity)
        {
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
            return requested;
        }

        private static List<BatchEntry> Batch(JObject arguments)
        {
            if (!(arguments["moves"] is JArray moves))
                throw new HandsException("invalid_arguments", T("moves 必须是 1–20 个物品移动的数组。"));
            if (moves.Count < 1 || moves.Count > 20)
                throw new HandsException("invalid_arguments", T("一次 move_items 可以移动 1–20 个物品堆，更多请分几次调用。"));
            var entries = new List<BatchEntry>();
            foreach (var token in moves)
            {
                if (!(token is JObject entry))
                    throw new HandsException("invalid_arguments", T("moves 中的每一项都必须是包含 item_id 和 source_container_id 的对象。"));
                ValidateKeys(entry, "item_id", "source_container_id", "quantity");
                entries.Add(new BatchEntry
                {
                    ItemId = Text(entry, "item_id"),
                    SourceContainerId = Text(entry, "source_container_id"),
                    Quantity = entry["quantity"] == null ? 0 : Number(entry, "quantity", 0, 0, int.MaxValue)
                });
            }
            return entries;
        }

        private void Record(string message)
        {
            history.Enqueue(message);
            while (history.Count > 50) history.Dequeue();
        }

        private sealed class BatchEntry
        {
            public string ItemId;
            public string SourceContainerId;
            public int Quantity;
        }

        private sealed class BatchMove
        {
            public string ItemId;
            public string ItemName;
            public string SourceContainerId;
            public int Requested;
            public int Moved;
            public string Code = "ok";
            public string Detail;
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

        // Fridge items are only ready for sale once they have reached the target cold state for their compartment;
        // un-frozen ice cream, un-chilled drinks/spirits, or chemical containers being cooled stay in_use.
        public static bool IsFridgeItemReady(string fridgeRole, string identifier, IEnumerable<string> types, bool frozen, bool chilled, bool iceCold)
        {
            if (fridgeRole != "chill" && fridgeRole != "freeze") return true;
            if (string.Equals(identifier, "beis_icecream", StringComparison.OrdinalIgnoreCase))
                return frozen;
            if (string.Equals(identifier, "nudka", StringComparison.OrdinalIgnoreCase))
                return fridgeRole == "freeze" ? iceCold : chilled || iceCold;
            bool chillable = string.Equals(identifier, "bottled_water_premium", StringComparison.OrdinalIgnoreCase) ||
                types != null && types.Any(t => string.Equals(t, "BREVAGE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "WATER_RATION", StringComparison.OrdinalIgnoreCase));
            if (chillable) return fridgeRole == "chill" && chilled;
            return false;
        }

        private static Dictionary<string, ItemInfo> BuildContainerOwners(WorldSnapshot world, Dictionary<string, ItemInfo> itemsById)
        {
            var owners = new Dictionary<string, ItemInfo>();
            foreach (var container in world.Containers)
            {
                if (!string.IsNullOrEmpty(container.Id) && !string.IsNullOrEmpty(container.OwnerItemId) &&
                    itemsById.TryGetValue(container.OwnerItemId, out var owner))
                    owners[container.Id] = owner;
            }
            foreach (var item in world.Items)
            {
                if (!string.IsNullOrEmpty(item.ContentContainerId))
                    owners[item.ContentContainerId] = item;
            }
            return owners;
        }

        // A pinned item itself is never for sale. Inside a pinned ancestor, only warehouse storage (STORAGE)
        // and sellable machine compartments (output, wine rack slots, or ready fridge items) remain eligible;
        // pinned non-storage, non-machine containers (such as reagent kits or tablet boxes) hold their contents back.
        private static bool IsHeldByPin(ItemInfo item, Dictionary<string, ContainerInfo> containersById, Dictionary<string, ItemInfo> ownerByContainerId)
        {
            if (item.Pinned == true) return true;
            var path = new List<ContainerInfo>();
            var visited = new HashSet<string>();
            var current = item.ContainerId;
            while (!string.IsNullOrEmpty(current) && visited.Add(current) && containersById.TryGetValue(current, out var container))
            {
                path.Add(container);
                current = container.ParentId;
            }
            for (int i = 0; i < path.Count; i++)
            {
                if (!ownerByContainerId.TryGetValue(path[i].Id, out var owner) || owner.Pinned != true) continue;
                var role = CompartmentRole(path, i, ownerByContainerId, owner);
                if (IsStorage(owner, role)) continue;
                if (IsMachine(owner, role) && IsSellableMachineCompartment(role, item)) continue;
                return true;
            }
            return false;
        }

        private static string CompartmentRole(List<ContainerInfo> path, int ownerIndex, Dictionary<string, ItemInfo> ownerByContainerId, ItemInfo owner)
        {
            for (int i = ownerIndex; i >= 0; i--)
            {
                if (ownerByContainerId.TryGetValue(path[i].Id, out var stepOwner) && !ReferenceEquals(stepOwner, owner))
                    break;
                if (!string.IsNullOrEmpty(path[i].Role)) return path[i].Role;
            }
            return null;
        }

        private static bool IsStorage(ItemInfo owner, string role)
            => owner.Types != null && owner.Types.Any(t => string.Equals(t, "STORAGE", StringComparison.OrdinalIgnoreCase)) ||
                role != null && role.StartsWith("storage:", StringComparison.OrdinalIgnoreCase);

        private static bool IsMachine(ItemInfo owner, string role)
            => owner.Types != null && owner.Types.Any(t => string.Equals(t, "MACHINE", StringComparison.OrdinalIgnoreCase)) ||
                !string.IsNullOrEmpty(role);

        private static bool IsSellableMachineCompartment(string role, ItemInfo item)
            => string.Equals(role, "output", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "output_disk", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "distillate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "wine", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(role, "chill", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(role, "freeze", StringComparison.OrdinalIgnoreCase)) && item.InUse != true;

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
