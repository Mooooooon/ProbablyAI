using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Hands
{
    // The adapter operates on live native objects, exclusively on Unity's main thread.
    public sealed class GameHandsBackend : IHandsBackend
    {
        private sealed class Binding
        {
            public GameInventory Inventory;
            public PixelWindow Window;
            public ContainerInfo Info;
        }
        private sealed class Region
        {
            public GameInventory Inventory;
            public string Name;
            public string Role;
        }
        private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly string session = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly Func<bool> available;
        private readonly Dictionary<GameItem, string> itemIds = new Dictionary<GameItem, string>();
        private readonly Dictionary<GameInventory, string> inventoryIds = new Dictionary<GameInventory, string>();
        private readonly Dictionary<PixelWindow, string> windowIds = new Dictionary<PixelWindow, string>();
        private readonly Dictionary<string, GameItem> items = new Dictionary<string, GameItem>();
        private readonly Dictionary<string, Binding> containers = new Dictionary<string, Binding>();
        private readonly HashSet<GameInventory> visited = new HashSet<GameInventory>();
        private WorldSnapshot world;
        private int sequence;
        private bool active = true;

        public GameHandsBackend(Func<bool> available) => this.available = available;
        public void Invalidate() => active = false;

        private void EnsureReady()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new HandsException("wrong_thread", T("物品操作必须在游戏主线程执行。"));
            if (!active || !available() || EmporiumEntry.Instance == null)
                throw new HandsException("store_unavailable", T("当前无法操作当铺，请先关闭暂停界面、返回当铺并等待载入完成。"));
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem || UIDragHandler.isDraggingAnyUI)
                throw new HandsException("player_dragging", T("你正在拖动物品或窗口，请松开后重试。"));
        }

        public WorldSnapshot Capture()
        {
            EnsureReady();
            items.Clear(); containers.Clear(); visited.Clear();
            world = new WorldSnapshot();
            var e = EmporiumEntry.Instance;
            AddLeaf(e.invElement, e.invWindow, "inventory", T("物品栏（主储藏区）"), null, null, null, 0);
            AddLeaf(e.backInvinvElement, e.backInvWindow, "counter", T("柜台（交易区）"), null, null, null, 0);
            AddLeaf(e.frontInvinvElement, e.frontInvWindow, "customer", T("窗口（客人储存区）"), null, null, null, 0);
            AddWindow(e.dossier?.contentWindow, e.dossier, T("档案架"), null, "documents", 0);
            AddWindow(e.trashcan?.contentWindow, e.trashcan, T("垃圾桶"), null, "trash", 0);
            AddLeaf(e.showcaseElement, e.showcaseWindow, "showcase", T("展示柜"), null, null, null, 0);
            AddLeaf(e.hiddenElement, e.hiddenWindow, "hidden", T("隐藏储藏区"), null, null, null, 0);
            return world;
        }

        private bool Limit(int depth)
        {
            if (depth <= 64 && world.Items.Count < 10000 && world.Containers.Count < 2000) return false;
            world.Truncated = true;
            return true;
        }

        private string AddWindow(PixelWindow window, GameItem owner, string path, string parentId, string fixedId, int depth)
        {
            if (window == null || Limit(depth)) return null;
            bool locked = window.isLocked || owner != null && owner.IsTag("LOCKED_CONTAINER_TAG") && owner.GetTagReadonly("LOCK_STATUS").GetBool();
            var regions = new List<Region>();
            if (!locked) Collect(window.childElement, regions, null, null, new HashSet<object>(), 0);
            if (regions.Count == 0 && !locked) return null; // A document's text window is not a storage container.
            string id = fixedId ?? Stable(windowIds, window, "c");
            if (containers.ContainsKey(id)) return id;
            if (regions.Count == 1 && !locked)
            {
                AddLeaf(regions[0].Inventory, window, id, path, parentId, owner, regions[0].Role, depth);
                return id;
            }
            var group = new ContainerInfo
            {
                Id = id, Name = LastName(path), Path = path, ParentId = parentId,
                OwnerItemId = parentId == null || owner == null ? null : Stable(itemIds, owner, "i"),
                Kind = "group", Locked = locked, IsOpen = window.IsVisible()
            };
            Register(new Binding { Window = window, Info = group });
            int index = 0;
            foreach (var region in regions)
            {
                var name = string.IsNullOrWhiteSpace(region.Name) ? T("分区 ") + (++index) : region.Name;
                AddLeaf(region.Inventory, window, Stable(inventoryIds, region.Inventory, "c"), path + " → " + name,
                    id, owner, region.Role, depth + 1);
            }
            return id;
        }

        private void AddLeaf(GameInventory inventory, PixelWindow window, string id, string path, string parentId, GameItem owner, string role, int depth)
        {
            if (inventory == null || Limit(depth) || !visited.Add(inventory)) return;
            bool locked = window != null && window.isLocked;
            var info = new ContainerInfo
            {
                Id = id, Name = LastName(path), Path = path, ParentId = parentId,
                OwnerItemId = parentId == null || owner == null ? null : Stable(itemIds, owner, "i"),
                Kind = inventory is GameSlotInventory ? "slot" : "grid", Role = role, Locked = locked,
                InsertLocked = inventory.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(inventory),
                RemoveLocked = inventory.IsRemoveLocked(), IsOpen = window != null && window.IsVisible()
            };
            Register(new Binding { Inventory = inventory, Window = window, Info = info });
            if (locked) return;
            foreach (var item in inventory.childItems.ToArray())
            {
                if (item == null || item.unitCount < 1 || item.parentInventory != inventory || Limit(depth + 1)) continue;
                if (item is GameItemElement element && element.IsDestroyed()) continue;
                var itemId = Stable(itemIds, item, "i");
                if (items.ContainsKey(itemId)) continue;
                items[itemId] = item;
                var name = Plain(item.GetDisplayName(true));
                if (string.IsNullOrWhiteSpace(name)) name = item.identifier ?? T("未命名物品");
                bool charging = IsCharging(item);
                // A battery in a machine's power compartment is installed for the machine's own use,
                // not loose stock the assistant may pick for a customer.
                bool installed = role == "power" && item.IsTag("power_source_item");
                // Pinned items stay put during sorting/unloading, usually because the owner still needs them.
                bool pinned = ItemPinHelper.IsPinned(item);
                bool container = item.IsTag("CONTAINER_TAG") || item.IsTag("BACKPACK_TAG");
                // Stolen items carry a discovery heat percentage used by security inspections.
                bool stolen = StolenHelper.IsStolenItem(item);
                int heat = stolen ? item.GetTagReadonly(StolenHelper.STOLEN_VALUE_INT).GetInt() : 0;
                var itemInfo = new ItemInfo
                {
                    Id = itemId, Identifier = item.identifier, Name = name, Path = path + " → " + name,
                    ContainerId = id, Quantity = item.unitCount, Owned = GeneralHelper.IsItemOwned(item),
                    Removable = item.MayRemove() && item.MaxNumRemove() > 0,
                    Container = container ? true : (bool?)null,
                    Pinned = pinned ? true : (bool?)null,
                    Stolen = stolen ? true : (bool?)null,
                    Heat = stolen ? heat : (int?)null,
                    Charging = charging ? true : (bool?)null,
                    InUse = charging || installed ? true : (bool?)null,
                    Description = Plain(item.shortDescription), Types = item.itemTypes?.ToList() ?? new List<string>()
                };
                world.Items.Add(itemInfo);
                info.ItemCount++;
                itemInfo.ContentContainerId = AddWindow(item.contentWindow, item, itemInfo.Path, id, null, depth + 1);
                if (itemInfo.ContentContainerId != null) itemInfo.Container = true;
            }
        }

        private static bool IsCharging(GameItem item)
        {
            if (item == null || !item.IsTag("power_source_item")) return false;
            var machine = item.parentInventory?.GetParentItem();
            if (machine == null || machine.identifier != "recharger") return false;
            if (PlayerStore.Instance == null || !PlayerStore.Instance.isPowerOn) return false;
            try { return !PowerHelper.IsPowerSourceFull(item); }
            catch (Exception) { return false; }
        }

        private void Register(Binding binding)
        {
            containers.Add(binding.Info.Id, binding);
            world.Containers.Add(binding.Info);
            if (binding.Info.ParentId != null && containers.TryGetValue(binding.Info.ParentId, out var parent))
                parent.Info.Children.Add(binding.Info.Id);
        }

        private void Collect(PixelElement element, List<Region> regions, string name, string role, HashSet<object> seen, int depth)
        {
            if (element == null || depth > 64 || !seen.Add(element)) return;
            if (element is GameInventory inventory)
            {
                if (!regions.Any(r => ReferenceEquals(r.Inventory, inventory))) regions.Add(new Region { Inventory = inventory, Name = name, Role = role });
            }
            else if (element is MachineryPanelRow row)
            {
                foreach (var panel in row.Panels)
                    if (!panel.isHidden) Collect(panel.content, regions, Plain(panel.label?.GetText()) ?? RoleName(panel.role), panel.role, seen, depth + 1);
            }
            else if (element is MachineryTabs tabs)
            {
                for (int i = 0; i < tabs.count; i++)
                    Collect(tabs.GetPage(i), regions, (name == null ? "" : name + " / ") + Plain(tabs.GetCaption(i)),
                        (role ?? "storage") + ":tab" + (i + 1), seen, depth + 1);
            }
            else if (element is MachineryIntake intake) Collect(intake.content, regions, name, role, seen, depth + 1);
            else if (element is MachineryPanel panel) Collect(panel.content, regions, name, role, seen, depth + 1);
            else if (element is GraphNodeStorage graph) CollectGraph(graph, regions, name, role, seen, depth + 1);
            else if (element is MachineryContainer container)
            {
                var children = new List<GraphNodeStorage>();
                container.CollectStorage(children);
                foreach (var child in children) CollectGraph(child, regions, name, role, seen, depth + 1);
            }
        }

        private void CollectGraph(GraphNodeStorage graph, List<Region> regions, string name, string role, HashSet<object> seen, int depth)
        {
            if (graph == null || graph is GameItem || depth > 64) return;
            if (graph is GameInventory inventory)
            {
                if (!regions.Any(r => ReferenceEquals(r.Inventory, inventory))) regions.Add(new Region { Inventory = inventory, Name = name, Role = role });
                return;
            }
            if (graph is PixelElement pixel && !seen.Contains(pixel)) { Collect(pixel, regions, name, role, seen, depth + 1); return; }
            foreach (var child in graph.children)
                if (child != null && seen.Add(child)) CollectGraph(child, regions, name, role, seen, depth + 1);
        }

        public MoveCheck CheckMove(string itemId, string sourceId, string destinationId, int quantity)
        {
            EnsureReady();
            var item = NativeItem(itemId);
            var source = NativeContainer(sourceId);
            var target = NativeContainer(destinationId);
            // Verify the native source, not just the snapshot: a batch may run after an earlier
            // move in the same call changed where the item actually is.
            if (source.Inventory == null) return Rejected("source_changed", T("物品来源不是具体容器分区，请重新查询物品位置。"));
            if (!ReferenceEquals(item.parentInventory, source.Inventory)) return Rejected("source_changed", T("物品已不在原位置，请重新查询后再移动。"));
            if (target.Inventory == null) return Rejected(T("请选择机器的具体分区或容器分页。"));
            if (!item.MayRemove() || item.MaxNumRemove() < 1) return Rejected(T("游戏禁止取出此物品，可能被锁定、固定或处于工作中。"));
            if (target.Window != null && target.Window.isLocked || target.Inventory.IsInsertLocked()) return Rejected(T("目标容器已锁定或禁止放入。"));
            if (!GeneralHelper.MayPlayerInsertInto(target.Inventory)) return Rejected(T("游戏不允许向这个容器放入物品。"));
            if (!target.Inventory.MayHaveValidInventorySlot(item)) return Rejected(T("目标不接受该物品：请检查所有权、材料类型、尺寸和容器限制。"));
            var slot = target.Inventory.TryFindOneValidInventorySlot(item);
            if (slot == null || slot.numTransfer < 1) return Rejected(T("目标没有合适的空位，或此物品当前无法放入。"));
            if (!ReferenceEquals(slot.inventory, target.Inventory))
                return Rejected(T("游戏将自动转入另一个子容器。请查询并明确选择该子容器后重试。"));
            return new MoveCheck { Allowed = true, FirstSlotQuantity = Math.Min(quantity, slot.numTransfer) };
        }

        public MoveOutcome Move(string itemId, string sourceId, string destinationId, int quantity)
        {
            EnsureReady();
            var item = NativeItem(itemId);
            var target = NativeContainer(destinationId);
            int moved = 0;
            string detail = null;
            // Each placement uses the exact native destination and native stack/split callbacks.
            // Never pre-expel, create, destroy, reassign ownership, or force an item into a slot.
            for (int step = 0; step < 40 && moved < quantity; step++)
            {
                MoveCheck check;
                try { check = CheckMove(itemId, sourceId, destinationId, quantity - moved); }
                catch (HandsException error) when (moved > 0) { detail = error.Message; break; }
                if (!check.Allowed) { detail = check.Reason; break; }
                var slot = target.Inventory.TryFindOneValidInventorySlot(item);
                if (slot == null || !ReferenceEquals(slot.inventory, target.Inventory)) break;
                int accepted = slot.TryAcceptOnce(quantity - moved);
                if (accepted < 1) { detail = T("原生转移未继续，请刷新列表检查。"); break; }
                moved += accepted;
            }
            return new MoveOutcome { Moved = moved, Detail = detail };
        }

        public void SetOpen(string containerId, bool open)
        {
            EnsureReady();
            var binding = NativeContainer(containerId);
            if (binding.Window == null) throw new HandsException("no_window", T("这个容器没有可显示的窗口。"));
            if (binding.Window.isLocked) throw new HandsException("container_locked", T("容器已锁定。"));
            if (open) { binding.Window.Show(); binding.Window.ToFront(); }
            else binding.Window.Hide();
        }

        // Only resolve IDs from the latest Capture; callers must refresh the world first.
        internal GameItem ResolveCurrentItem(string id)
        {
            EnsureReady();
            return NativeItem(id);
        }

        private GameItem NativeItem(string id)
            => items.TryGetValue(id, out var item) && item.parentInventory != null ? item : throw new HandsException("item_not_found", T("物品已不在当前容器树中。"));
        private Binding NativeContainer(string id)
            => containers.TryGetValue(id, out var binding) ? binding : throw new HandsException("container_not_found", T("容器已失效，请重新获取。"));
        private static MoveCheck Rejected(string reason) => new MoveCheck { Reason = reason };
        private static MoveCheck Rejected(string code, string reason) => new MoveCheck { Code = code, Reason = reason };
        private string Stable<T>(Dictionary<T, string> registry, T value, string prefix)
        {
            if (!registry.TryGetValue(value, out var id)) registry[value] = id = prefix + "-" + session + "-" + (++sequence);
            return id;
        }
        private static string Plain(string value) => value == null ? null : Regex.Replace(value, "<[^>]*>", "");
        private static string LastName(string path) => path.Split(new[] { " → " }, StringSplitOptions.None).Last();
        private static string RoleName(string role)
        {
            switch (role)
            {
                case "input": return T("原料");
                case "output": return T("成品");
                case "power": return T("电池");
                case "module": return T("模块");
                case "note": return T("说明文件");
                default: return role ?? T("储藏");
            }
        }
    }
}
