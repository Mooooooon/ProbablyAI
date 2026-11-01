using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Services;

internal static partial class Program
{
    private static async Task RunHandsTests()
    {
        await Check("nested archive search distinguishes identical note names by path and ID", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("find_items", new JObject { ["query"] = "笔记", ["container_id"] = "documents" }));
            var entries = (JArray)result["data"]["page"]["entries"];
            Assert(entries.Count == 2 && entries.Select(i => (string)i["id"]).Distinct().Count() == 2, "same-name notes merged");
            Assert(entries.All(i => ((string)i["path"]).Contains("导师笔记")), "nested paths lost");
        });
        await Check("container tree and direct contents do not flatten a machine's partitions", () =>
        {
            var hands = new HandsService(new FakeHands());
            var root = Doc(hands.Execute("list_container", new JObject { ["container_id"] = "furnace" }));
            Assert(((JArray)root["data"]["children"]).Count == 2, "machine compartments lost");
            Assert(((JArray)root["data"]["items"]["entries"]).Count == 0, "group flattened");
            var search = Doc(hands.Execute("list_containers", new JObject { ["query"] = "input" }));
            Assert((int)search["data"]["page"]["total"] == 1, "role search failed");
        });
        await Check("pagination reports exact totals and leaves names unchanged", () =>
        {
            var hands = new HandsService(new FakeHands());
            var result = Doc(hands.Execute("find_items", new JObject { ["query"] = "笔记", ["limit"] = 1, ["offset"] = 0 }));
            Assert((int)result["data"]["page"]["total"] == 2 && (bool)result["data"]["page"]["has_more"], "pagination metadata");
            Assert(((JArray)result["data"]["page"]["entries"]).Count == 1, "limit ignored");
        });
        await Check("item JSON exposes a charging battery flag only when true", () =>
        {
            var charging = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "battery", Name = "电池", Charging = true, InUse = true }));
            Assert((bool)charging["charging"] && (bool)charging["in_use"], "battery flags lost");
            var idle = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "ore", Name = "矿石" }));
            Assert(idle["charging"] == null && idle["in_use"] == null, "idle item marked in use");
        });
        await Check("item JSON exposes pinned and container flags only when true", () =>
        {
            var pinned = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "pinned", Name = "固定矿石", Pinned = true }));
            Assert((bool)pinned["pinned"], "pinned flag lost");
            var container = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "bag", Name = "背包", Container = true }));
            Assert((bool)container["container"], "container flag lost");
            var normal = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "ore", Name = "矿石" }));
            Assert(normal["pinned"] == null && normal["container"] == null, "unset flags serialized");
        });
        await Check("item JSON exposes stolen and heat only for stolen items", () =>
        {
            var stolen = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "ring", Name = "戒指", Stolen = true, Heat = 75 }));
            Assert((bool)stolen["stolen"] && (int)stolen["heat"] == 75, "stolen marker or heat lost");
            var normal = JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "ore", Name = "矿石" }));
            Assert(normal["stolen"] == null && normal["heat"] == null, "clean item marked stolen");
        });
        await Check("stolen items are excluded from goods search unless explicitly requested", () =>
        {
            var backend = new FakeHands();
            backend.World.Items.Add(new ItemInfo { Id = "ring", Name = "赃物戒指", ContainerId = "inventory",
                Path = "物品栏（主储藏区） → 赃物戒指", Quantity = 1, Owned = true, Removable = true, Stolen = true, Heat = 75 });
            var hands = new HandsService(backend);
            var hidden = Doc(hands.Execute("find_items", new JObject { ["query"] = "戒指" }));
            Assert(((JArray)hidden["data"]["page"]["entries"]).Count == 0, "stolen item offered as goods");
            var all = Doc(hands.Execute("find_items", new JObject { ["query"] = "戒指", ["include_stolen"] = true }));
            Assert(((JArray)all["data"]["page"]["entries"]).Count == 1, "explicit stolen search hid item");
            var inspected = Doc(hands.Execute("inspect_item", new JObject { ["item_id"] = "ring" }));
            Assert((bool)inspected["data"]["stolen"] && (int)inspected["data"]["heat"] == 75, "inspect_item lost stolen heat");
            var listed = Doc(hands.Execute("list_container", new JObject { ["container_id"] = "inventory" }));
            var entry = ((JArray)listed["data"]["items"]["entries"]).Single(i => (string)i["id"] == "ring");
            Assert((bool)entry["stolen"] && (int)entry["heat"] == 75, "list_container lost stolen heat");
        });
        await Check("pinned items and non-storage non-machine pinned container contents are excluded while pinned storage and machine output remain sellable", () =>
        {
            var backend = new FakeHands();
            backend.World.Items.First(i => i.Id == "ore").Pinned = true;
            backend.World.Items.First(i => i.Id == "bag").Pinned = true;
            backend.World.Items.Add(new ItemInfo
            {
                Id = "reagent-kit", Name = "试剂盒", Identifier = "water_test_container", ContainerId = "inventory",
                Path = "物品栏 → 试剂盒", Quantity = 1, Owned = true, Removable = true, Container = true,
                Pinned = true, ContentContainerId = "reagent-kit-inner"
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "reagent-kit-inner", Name = "试剂盒内容", Path = "物品栏 → 试剂盒",
                ParentId = "inventory", OwnerItemId = "reagent-kit", Kind = "grid"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "test-strip", Name = "水质试纸", Identifier = "water_test_strip", ContainerId = "reagent-kit-inner",
                Path = "物品栏 → 试剂盒 → 水质试纸", Quantity = 5, Owned = true, Removable = true
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "tablet-box", Name = "净水药片盒", Identifier = "water_tablet_container", ContainerId = "inventory",
                Path = "物品栏 → 净水药片盒", Quantity = 1, Owned = true, Removable = true, Container = true,
                Pinned = true, ContentContainerId = "tablet-box-inner"
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "tablet-box-inner", Name = "净水药片盒内容", Path = "物品栏 → 净水药片盒",
                ParentId = "inventory", OwnerItemId = "tablet-box", Kind = "grid"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "water-tablet", Name = "净水药片", Identifier = "water_tablet", ContainerId = "tablet-box-inner",
                Path = "物品栏 → 净水药片盒 → 净水药片", Quantity = 4, Owned = true, Removable = true
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "storage-bay", Name = "储物仓", Identifier = "storage_bay", ContainerId = "inventory",
                Path = "物品栏 → 储物仓", Quantity = 1, Owned = true, Removable = true, Container = true,
                Pinned = true, ContentContainerId = "storage-bay-inner", Types = new List<string> { "STORAGE" }
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "storage-bay-inner", Name = "储物仓内容", Path = "物品栏 → 储物仓",
                ParentId = "inventory", OwnerItemId = "storage-bay", Kind = "grid"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "stored-ingot", Name = "储藏金属锭", Identifier = "metal_ingot", ContainerId = "storage-bay-inner",
                Path = "物品栏 → 储物仓 → 储藏金属锭", Quantity = 3, Owned = true, Removable = true
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "furnace-item", Name = "熔炉设备", Identifier = "furnace", ContainerId = "inventory",
                Path = "物品栏 → 熔炉", Quantity = 1, Owned = true, Removable = true, Container = true,
                Pinned = true, ContentContainerId = "furnace", Types = new List<string> { "MACHINE" }
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "furnace-module", Name = "模块", Path = "物品栏 → 熔炉 → 模块",
                ParentId = "furnace", OwnerItemId = "furnace-item", Kind = "slot", Role = "module"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "raw-scrap", Name = "炉内废金属", Identifier = "scrap_metal", ContainerId = "input",
                Path = "物品栏 → 熔炉 → 原料 → 炉内废金属", Quantity = 2, Owned = true, Removable = true
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "efficiency-mod", Name = "效率模块", Identifier = "module_eff", ContainerId = "furnace-module",
                Path = "物品栏 → 熔炉 → 模块 → 效率模块", Quantity = 1, Owned = true, Removable = true
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "smelted-ingot", Name = "成品金属锭", Identifier = "metal_ingot", ContainerId = "output",
                Path = "物品栏 → 熔炉 → 成品 → 成品金属锭", Quantity = 2, Owned = true, Removable = true
            });
            var hands = new HandsService(backend);
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "矿石" }))["data"]["page"]["entries"]).Count == 0, "pinned item offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "背包" }))["data"]["page"]["entries"]).Count == 0, "pinned bag offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "storage_bay" }))["data"]["page"]["entries"]).Count == 0, "pinned storage container itself offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "熔炉设备" }))["data"]["page"]["entries"]).Count == 0, "pinned machine itself offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "试剂盒" }))["data"]["page"]["entries"]).Count == 0, "pinned reagent kit offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "水质试纸" }))["data"]["page"]["entries"]).Count == 0, "strips inside pinned reagent kit offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "净水药片" }))["data"]["page"]["entries"]).Count == 0, "tablets inside pinned tablet box offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "炉内废金属" }))["data"]["page"]["entries"]).Count == 0, "input material inside pinned machine offered as goods");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "效率模块" }))["data"]["page"]["entries"]).Count == 0, "module inside pinned machine offered as goods");
            var ingots = ((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "金属锭" }))["data"]["page"]["entries"]).Select(e => (string)e["id"]).ToArray();
            Assert(ingots.Length == 2 && ingots.Contains("stored-ingot") && ingots.Contains("smelted-ingot"), "items in pinned STORAGE or MACHINE output should remain sellable");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "水质试纸", ["include_pinned"] = true }))["data"]["page"]["entries"]).Count == 1, "explicit pinned search hid reagent kit contents");
            Assert(((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "矿石", ["include_pinned"] = true }))["data"]["page"]["entries"]).Count == 1, "explicit pinned search hid item");
        });
        await Check("fridge pre-freeze items stay in_use while frozen items and wine rack or beer case goods follow pin rules", () =>
        {
            Assert(!HandsService.IsFridgeItemReady("freeze", "beis_icecream", new[] { "FOOD" }, frozen: false, chilled: false, iceCold: false), "un-frozen ice cream marked ready");
            Assert(HandsService.IsFridgeItemReady("freeze", "beis_icecream", new[] { "FOOD" }, frozen: true, chilled: false, iceCold: false), "frozen ice cream not ready in freeze");
            Assert(HandsService.IsFridgeItemReady("chill", "beis_icecream", new[] { "FOOD" }, frozen: true, chilled: false, iceCold: false), "frozen ice cream not ready in chill");
            Assert(!HandsService.IsFridgeItemReady("chill", "beis_icecream", new[] { "FOOD" }, frozen: false, chilled: true, iceCold: false), "un-frozen ice cream in chill marked ready");
            Assert(!HandsService.IsFridgeItemReady("freeze", "nudka", new[] { "ALCOHOL", "BREVAGE" }, frozen: false, chilled: true, iceCold: false), "nudka in freeze without iceCold marked ready");
            Assert(HandsService.IsFridgeItemReady("freeze", "nudka", new[] { "ALCOHOL", "BREVAGE" }, frozen: false, chilled: false, iceCold: true), "ice-cold nudka in freeze not ready");
            Assert(HandsService.IsFridgeItemReady("chill", "nudka", new[] { "ALCOHOL", "BREVAGE" }, frozen: false, chilled: true, iceCold: false), "chilled nudka in chill not ready");
            Assert(!HandsService.IsFridgeItemReady("chill", "red_imp_beer", new[] { "BREVAGE" }, frozen: false, chilled: false, iceCold: false), "un-chilled beer marked ready");
            Assert(HandsService.IsFridgeItemReady("chill", "red_imp_beer", new[] { "BREVAGE" }, frozen: false, chilled: true, iceCold: false), "chilled beer not ready");
            Assert(!HandsService.IsFridgeItemReady("chill", "chem_jar_small", new[] { "CHEMICAL" }, frozen: false, chilled: false, iceCold: false), "cooling chemical container marked ready");

            var backend = new FakeHands();
            backend.World.Items.Add(new ItemInfo
            {
                Id = "fridge-item", Name = "冰箱设备", Identifier = "fridge", ContainerId = "inventory",
                Path = "物品栏 → 冰箱", Quantity = 1, Owned = true, Removable = true, Container = true,
                Pinned = true, ContentContainerId = "fridge", Types = new List<string> { "MACHINE" }
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "fridge-freeze", Name = "冷冻", Path = "物品栏 → 冰箱 → 冷冻",
                ParentId = "fridge", OwnerItemId = "fridge-item", Kind = "grid", Role = "freeze"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "icecream-soft", Name = "未冻冰激凌", Identifier = "beis_icecream", ContainerId = "fridge-freeze",
                Path = "物品栏 → 冰箱 → 冷冻 → 未冻冰激凌", Quantity = 1, Owned = true, Removable = true,
                InUse = true, Types = new List<string> { "FOOD" }
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "icecream-frozen", Name = "冻好冰激凌", Identifier = "beis_icecream", ContainerId = "fridge-freeze",
                Path = "物品栏 → 冰箱 → 冷冻 → 冻好冰激凌", Quantity = 1, Owned = true, Removable = true,
                Types = new List<string> { "FOOD" }
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "wine-rack", Name = "陈酿酒架", Identifier = "wine_rack", ContainerId = "inventory",
                Path = "物品栏 → 陈酿酒架", Quantity = 1, Owned = true, Removable = true, Container = true,
                ContentContainerId = "wine-rack-group", Types = new List<string> { "MACHINE" }
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "wine-rack-group", Name = "陈酿酒架", Path = "物品栏 → 陈酿酒架",
                ParentId = "inventory", OwnerItemId = "wine-rack", Kind = "group"
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "wine-rack-slots", Name = "酒槽", Path = "物品栏 → 陈酿酒架 → 酒槽",
                ParentId = "wine-rack-group", OwnerItemId = "wine-rack", Kind = "grid", Role = "wine"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "vintage-wine", Name = "陈年红酒", Identifier = "wine_bottle", ContainerId = "wine-rack-slots",
                Path = "物品栏 → 陈酿酒架 → 酒槽 → 陈年红酒", Quantity = 1, Owned = true, Removable = true,
                Types = new List<string> { "ALCOHOL", "BREVAGE" }
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "beer-case", Name = "红魔鬼啤酒箱", Identifier = "beerCase", ContainerId = "inventory",
                Path = "物品栏 → 红魔鬼啤酒箱", Quantity = 1, Owned = true, Removable = true, Container = true,
                ContentContainerId = "beer-case-inner", Types = new List<string> { "SUBSTANCE", "ALCOHOL", "BREVAGE" }
            });
            backend.World.Containers.Add(new ContainerInfo
            {
                Id = "beer-case-inner", Name = "啤酒箱内容", Path = "物品栏 → 红魔鬼啤酒箱",
                ParentId = "inventory", OwnerItemId = "beer-case", Kind = "grid"
            });
            backend.World.Items.Add(new ItemInfo
            {
                Id = "beer-can", Name = "红魔鬼啤酒罐", Identifier = "red_imp_beer", ContainerId = "beer-case-inner",
                Path = "物品栏 → 红魔鬼啤酒箱 → 红魔鬼啤酒罐", Quantity = 6, Owned = true, Removable = true,
                Types = new List<string> { "SUBSTANCE", "ALCOHOL", "BREVAGE" }
            });

            var hands = new HandsService(backend);
            var iceCreams = ((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "冰激凌" }))["data"]["page"]["entries"]).Select(e => (string)e["id"]).ToArray();
            Assert(iceCreams.Length == 1 && iceCreams[0] == "icecream-frozen", "only frozen ice cream should be offered from fridge");

            var unpinnedWine = ((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "酒" }))["data"]["page"]["entries"]).Select(e => (string)e["id"]).ToArray();
            Assert(unpinnedWine.Contains("wine-rack") && unpinnedWine.Contains("vintage-wine") && unpinnedWine.Contains("beer-case") && unpinnedWine.Contains("beer-can"),
                "unpinned wine rack and beer case should allow selling the whole container or inner bottles/cans");

            backend.World.Items.First(i => i.Id == "wine-rack").Pinned = true;
            backend.World.Items.First(i => i.Id == "beer-case").Pinned = true;
            var pinnedWine = ((JArray)Doc(hands.Execute("find_items", new JObject { ["query"] = "酒" }))["data"]["page"]["entries"]).Select(e => (string)e["id"]).ToArray();
            Assert(!pinnedWine.Contains("wine-rack") && pinnedWine.Contains("vintage-wine") && !pinnedWine.Contains("beer-case") && !pinnedWine.Contains("beer-can"),
                "pinned wine rack should still allow selling wine inside while pinned beer case holds its cans back");
        });
        await Check("installed power batteries are hidden from goods search unless explicitly requested", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            var visible = Doc(hands.Execute("find_items", new JObject { ["query"] = "电池" }));
            var ids = ((JArray)visible["data"]["page"]["entries"]).Select(e => (string)e["id"]).ToArray();
            Assert(ids.Length == 1 && ids[0] == "spare-battery", "in-use battery offered as goods");
            var listed = Doc(hands.Execute("list_container", new JObject { ["container_id"] = "fridge-power" }));
            Assert((bool)listed["data"]["items"]["entries"][0]["in_use"], "in_use flag missing from listing");
            var all = Doc(hands.Execute("find_items", new JObject { ["query"] = "电池", ["include_in_use"] = true }));
            Assert(((JArray)all["data"]["page"]["entries"]).Count == 2, "explicit in-use search hid batteries");
        });
        await Check("preview does not mutate game state", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("can_move_item", Move("ore", "inventory", "input", 3));
            Assert(result.Success && backend.Moves == 0 && backend.World.Items.First(i => i.Id == "ore").Quantity == 8, "preview mutated");
        });
        await Check("moving one exact nested note leaves its same-name sibling untouched", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("note-a", "mentor", "counter", 1));
            Assert(result.Success && backend.World.Items.First(i => i.Id == "note-a").ContainerId == "counter", "selected note not moved");
            Assert(backend.World.Items.First(i => i.Id == "note-b").ContainerId == "mentor", "sibling moved");
        });
        await Check("zero quantity means one selected stack only", () =>
        {
            var backend = new FakeHands();
            var result = Doc(new HandsService(backend).Execute("move_item", Move("ore", "inventory", "counter", 0)));
            Assert((int)result["data"]["moved"] == 8 && backend.Moves == 1, "zero quantity not scoped to one stack");
        });
        await Check("a partial native transfer is not reported as full success", () =>
        {
            var backend = new FakeHands { TransferLimit = 2 };
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("move_item", Move("ore", "inventory", "input", 5)));
            Assert(!(bool)result["success"] && (string)result["code"] == "partial", "partial reported success");
            Assert((int)result["data"]["moved"] == 2 && (int)result["data"]["remaining"] == 3, "partial counts wrong");
            Assert(hands.History.Single().Contains("2/5"), "partial missing from audit");
        });
        await Check("zero native movement remains a failed operation", () =>
        {
            var backend = new FakeHands { TransferLimit = 0 };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "trash", 2));
            Assert(!result.Success && result.Code == "not_moved", "zero treated as success");
        });
        await Check("a stale source cannot silently move an item from its new location", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            Assert(hands.Execute("move_item", Move("note-a", "mentor", "counter", 1)).Success, "first move");
            var stale = hands.Execute("move_item", Move("note-a", "mentor", "trash", 1));
            Assert(stale.Code == "source_changed" && backend.Moves == 1, "stale move ran");
        });
        await Check("container into itself or any descendant is rejected", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("bag", "inventory", "bag-inner", 1));
            Assert(result.Code == "container_cycle" && backend.Moves == 0, "cycle accepted");
        });
        await Check("group destination requires a concrete machine compartment", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "furnace", 1));
            Assert(result.Code == "choose_compartment" && backend.Moves == 0, "ambiguous machine accepted");
        });
        await Check("locked containers and immovable items remain untouched", () =>
        {
            var backend = new FakeHands();
            backend.World.Containers.First(c => c.Id == "input").InsertLocked = true;
            var hands = new HandsService(backend);
            Assert(hands.Execute("move_item", Move("ore", "inventory", "input", 1)).Code == "container_locked", "locked input accepted");
            backend.World.Items.First(i => i.Id == "ore").Removable = false;
            Assert(hands.Execute("move_item", Move("ore", "inventory", "counter", 1)).Code == "item_locked", "fixed item accepted");
            backend.World.Containers.First(c => c.Id == "mentor").Locked = true;
            Assert(hands.Execute("list_container", new JObject { ["container_id"] = "mentor" }).Code == "container_locked", "locked contents read");
            Assert(backend.Moves == 0, "locked action mutated state");
        });
        await Check("native material and ownership rejection is returned unchanged", () =>
        {
            var backend = new FakeHands { Rejection = "目标不接受此材料 / 所有权不匹配" };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "input", 1));
            Assert(result.Code == "move_rejected" && result.Message == backend.Rejection && backend.Moves == 0, "native rules bypassed");
        });
        await Check("missing IDs, quantities and unknown parameters cannot execute moves", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            foreach (var change in new Action<JObject>[]
            {
                a => a["item_id"] = "old-session-id", a => a.Remove("quantity"), a => a["quantity"] = -1,
                a => a["quantity"] = "3", a => a["quantity"] = 99, a => a["quantity"] = long.MaxValue,
                a => a["arbitrary_code"] = "do something", a => a["source_container_id"] = "物品栏"
            })
            {
                var args = Move("ore", "inventory", "counter", 1); change(args);
                Assert(!hands.Execute("move_item", args).Success, "invalid parameters accepted");
            }
            Assert(backend.Moves == 0, "invalid move executed");
        });
        await Check("open/close uses the same container interface and honors locks", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            Assert(hands.Execute("open_container", new JObject { ["container_id"] = "mentor" }).Success && backend.Open, "open failed");
            Assert(hands.Execute("close_container", new JObject { ["container_id"] = "mentor" }).Success && !backend.Open, "close failed");
            Assert(hands.Execute("destroy_item", new JObject()).Code == "unknown_tool", "arbitrary actions exposed");
        });
        await Check("native exception does not claim success", () =>
        {
            var backend = new FakeHands { ThrowOnMove = true };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "counter", 1));
            Assert(result.Code == "game_error" && !result.Success, "exception hidden as success");
        });
        await Check("all tools declare object schemas without arbitrary extra properties", () =>
        {
            var tools = HandsProtocol.Tools();
            Assert(tools.Count == 9 && tools.Select(t => (string)t["function"]["name"]).Distinct().Count() == 9, "tool set changed unexpectedly");
            foreach (var tool in tools)
            {
                var schema = tool["function"]["parameters"];
                Assert((string)schema["type"] == "object" && !(bool)schema["additionalProperties"], "unsafe/incompatible schema");
                foreach (var required in (JArray)schema["required"]) Assert(schema["properties"][(string)required] != null, "required field missing");
            }
            var batch = tools.First(t => (string)t["function"]["name"] == "move_items")["function"]["parameters"]["properties"]["moves"];
            Assert((string)batch["type"] == "array" && (int)batch["maxItems"] == 20 && !(bool)batch["items"]["additionalProperties"] &&
                JToken.DeepEquals(batch["items"]["required"], new JArray("item_id", "source_container_id")), "batch move schema is not bounded or strict");
        });
        await Check("batch move places several stacks in one call with per-item results", () =>
        {
            var backend = new FakeHands { Captures = 0 };
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("move_items", Batch("counter", ("ore", "inventory", 0), ("note-a", "mentor", 1), ("spare-battery", "inventory", 0))));
            Assert((bool)result["success"] && (string)result["code"] == "ok", "batch not successful");
            Assert((int)result["data"]["total"] == 3 && (int)result["data"]["succeeded"] == 3 && (int)result["data"]["moved"] == 11, "batch totals wrong");
            var entries = (JArray)result["data"]["results"];
            Assert(entries.Count == 3 && entries.All(e => (string)e["code"] == "ok"), "per-item results missing");
            Assert(backend.Moves == 3, "batch did not move each stack once");
            Assert(backend.World.Items.First(i => i.Id == "note-a").ContainerId == "counter" &&
                backend.World.Items.First(i => i.Id == "ore").ContainerId == "counter", "goods not placed by the batch");
            Assert(backend.Captures == 1, "batch captured the world more than once");
            Assert(hands.History.Count == 1 && hands.History[0].Contains("3"), "batch did not record one summary");
        });
        await Check("batch move keeps going after a stale item and reports each stack", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("move_items", Batch("counter", ("ore", "inventory", 1), ("missing", "inventory", 1), ("note-a", "mentor", 1))));
            var entries = (JArray)result["data"]["results"];
            Assert(!(bool)result["success"] && (string)result["code"] == "partial", "partial batch reported success");
            Assert((string)entries[0]["code"] == "ok" && (string)entries[1]["code"] == "item_not_found" && (string)entries[2]["code"] == "ok", "per-item codes wrong");
            Assert((int)result["data"]["succeeded"] == 2 && (int)result["data"]["failed"] == 1 && (int)result["data"]["moved"] == 2, "batch summary wrong");
            Assert(backend.Moves == 2 && backend.World.Items.First(i => i.Id == "note-a").ContainerId == "counter", "one stale item blocked the rest");
            Assert(!string.IsNullOrEmpty((string)entries[1]["detail"]), "per-item failure detail missing");
        });
        await Check("batch move reports a native partial transfer per stack", () =>
        {
            var backend = new FakeHands { TransferLimit = 2 };
            var result = Doc(new HandsService(backend).Execute("move_items", Batch("input", ("ore", "inventory", 0))));
            var entry = ((JArray)result["data"]["results"])[0];
            Assert((string)result["code"] == "partial" && (string)entry["code"] == "partial" && (int)entry["moved"] == 2 && (int)entry["requested"] == 8,
                "partial native transfer hidden by the batch");
        });
        await Check("batch move rejects malformed lists, ambiguous targets and locked destinations before moving", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            var overlong = new JArray();
            for (int i = 0; i < 21; i++) overlong.Add(new JObject { ["item_id"] = "ore", ["source_container_id"] = "inventory" });
            foreach (var arguments in new JObject[]
            {
                new JObject { ["destination_container_id"] = "counter" },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = new JArray() },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = overlong },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = new JArray(new JObject { ["item_id"] = "ore" }) },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = new JArray(new JObject { ["item_id"] = "ore", ["source_container_id"] = "inventory", ["buy"] = true }) },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = new JArray(new JObject { ["item_id"] = "ore", ["source_container_id"] = "inventory", ["quantity"] = "2" }) },
                new JObject { ["destination_container_id"] = "counter", ["moves"] = new JArray(new JObject { ["item_id"] = "ore", ["source_container_id"] = "inventory" }), ["extra"] = 1 }
            })
                Assert(hands.Execute("move_items", arguments).Code == "invalid_arguments", "malformed batch accepted");
            Assert(hands.Execute("move_items", Batch("furnace", ("ore", "inventory", 1))).Code == "choose_compartment", "ambiguous machine accepted");
            backend.World.Containers.First(c => c.Id == "counter").InsertLocked = true;
            Assert(hands.Execute("move_items", Batch("counter", ("ore", "inventory", 1))).Code == "container_locked", "locked destination accepted");
            Assert(backend.Moves == 0, "rejected batch still moved items");
        });
        await Check("batch move returns the native rule rejection for every stack", () =>
        {
            var backend = new FakeHands { Rejection = "目标不接受此物品" };
            var result = Doc(new HandsService(backend).Execute("move_items", Batch("counter", ("ore", "inventory", 1), ("note-a", "mentor", 1))));
            Assert((string)result["code"] == "not_moved" && (int)result["data"]["failed"] == 2, "native rejection not per stack");
            Assert(((JArray)result["data"]["results"]).All(e => (string)e["code"] == "move_rejected"), "native rejection reason lost");
            Assert(backend.Moves == 0, "rejected batch executed a native move");
        });
        await Check("agent places several goods with one move_items call", async () =>
        {
            var backend = new FakeHands(); int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (round++ == 0) return Call("batch", "move_items", Batch("counter", ("note-a", "mentor", 1), ("note-b", "mentor", 1)));
                var data = JObject.Parse((string)messages.Last["content"])["data"];
                Assert((int)data["succeeded"] == 2, "batch result missing from the tool reply");
                return new ChatTurn { Text = "两份笔记都摆到柜台了。" };
            });
            var result = await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "把两份笔记摆到柜台", null, default);
            Assert(round == 2 && result.ToolCalls == 1 && result.Operations.Count == 1 && backend.Moves == 2, "batch placement needed extra model round trips");
            Assert(backend.World.Items.Where(i => i.Id.StartsWith("note-")).All(i => i.ContainerId == "counter"), "not all goods were placed");
        });
        await Check("OpenAI tool-call response is parsed and reasoning message is preserved", async () =>
        {
            using var http = Respond("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"reasoning_content\":\"provider-state\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}}]}}]}");
            using var client = new OpenAiApiClient(http);
            var turn = await client.CompleteTurnAsync(new ConnectionOptions(), new JArray(), HandsProtocol.Tools(), default);
            Assert(turn.ToolCalls.Single().Name == "list_containers" && (string)turn.Message["reasoning_content"] == "provider-state", "tool/reasoning lost");
        });
        await Check("malformed or duplicate wire tool calls are rejected before execution", async () =>
        {
            foreach (var calls in new[]
            {
                "\"bad\"", "[{}]", "[{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"move_item\",\"arguments\":{}}}]",
                "[{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}},{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}}]"
            })
            {
                using var http = Respond("{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":" + calls + "}}]}");
                using var client = new OpenAiApiClient(http);
                await ExpectAsync<ApiRequestException>(() => client.CompleteTurnAsync(new ConnectionOptions(), new JArray(), HandsProtocol.Tools(), default));
            }
        });
        await Check("agent executes query -> move -> report with matching tool results", async () =>
        {
            var backend = new FakeHands();
            int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                round++;
                if (round == 1) return Call("query", "find_items", new JObject { ["query"] = "笔记" });
                Assert((string)messages[2]["reasoning_content"] == "provider-state", "provider reasoning discarded");
                if (round == 2)
                {
                    Assert((string)messages[3]["tool_call_id"] == "query", "tool result id mismatch");
                    return Call("move", "move_item", Move("note-a", "mentor", "counter", 1));
                }
                Assert((int)JObject.Parse((string)messages.Last["content"])["data"]["moved"] == 1, "actual result missing");
                return new ChatTurn { Text = "已移动 1 份笔记。" };
            });
            var result = await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "移动笔记", null, default);
            Assert(result.ToolCalls == 2 && result.Operations.Count == 1 && backend.Moves == 1, "agent didn't complete tools");
        });
        await Check("agent cancellation between tools stops remaining physical actions", async () =>
        {
            var backend = new FakeHands();
            using var cancellation = new CancellationTokenSource();
            var transport = new FakeTransport((messages, token) =>
            {
                var turn = Call("one", "move_item", Move("note-a", "mentor", "counter", 1));
                turn.ToolCalls = turn.ToolCalls.Concat(new[] { new ChatToolCall { Id = "two", Name = "move_item", Arguments = Move("note-b", "mentor", "trash", 1).ToString() } }).ToArray();
                return turn;
            });
            await ExpectAsync<OperationCanceledException>(() => new HandsAgent(transport, new HandsService(backend))
                .RunAsync(new ConnectionOptions(), "移动两份笔记", _ => { if (backend.Moves == 1) cancellation.Cancel(); }, cancellation.Token));
            Assert(backend.Moves == 1 && backend.World.Items.First(i => i.Id == "note-b").ContainerId == "mentor", "cancelled action still ran");
        });
        await Check("invalid model arguments return a tool error without touching items", async () =>
        {
            var backend = new FakeHands(); int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (round++ == 0) { var turn = Call("bad", "move_item", new JObject()); turn.ToolCalls[0].Arguments = "not-json"; return turn; }
                Assert((string)JObject.Parse((string)messages.Last["content"])["code"] == "invalid_arguments", "tool error missing");
                return new ChatTurn { Text = "参数无效，未移动物品。" };
            });
            await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "试试", null, default);
            Assert(backend.Moves == 0, "malformed arguments mutated state");
        });
        await Check("agent loop has a finite round budget", async () =>
        {
            var backend = new FakeHands(); int round = 0;
            var transport = new FakeTransport((messages, token) => Call("loop-" + (++round), "list_containers", new JObject()));
            var result = await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "查看容器", null, default);
            Assert(round == 16 && backend.Moves == 0 && result.Reply.Contains("上限"), "unbounded tool loop");
        });
    }

    private static JObject Move(string item, string source, string target, int quantity)
        => new JObject { ["item_id"] = item, ["source_container_id"] = source, ["destination_container_id"] = target, ["quantity"] = quantity };
    private static JObject Batch(string destination, params (string Item, string Source, int Quantity)[] moves)
    {
        var array = new JArray();
        foreach (var move in moves)
            array.Add(new JObject { ["item_id"] = move.Item, ["source_container_id"] = move.Source, ["quantity"] = move.Quantity });
        return new JObject { ["destination_container_id"] = destination, ["moves"] = array };
    }
    private static JObject Doc(HandsResult value) => JObject.Parse(HandsProtocol.Json(value));
    private static ChatTurn Call(string id, string name, JObject arguments)
    {
        var calls = new[] { new ChatToolCall { Id = id, Name = name, Arguments = arguments.ToString() } };
        return new ChatTurn { ToolCalls = calls, Message = new JObject { ["role"] = "assistant", ["content"] = JValue.CreateNull(), ["reasoning_content"] = "provider-state",
            ["tool_calls"] = new JArray(new JObject { ["id"] = id, ["type"] = "function", ["function"] = new JObject { ["name"] = name, ["arguments"] = arguments.ToString() } }) } };
    }

    private sealed class FakeTransport : IChatTransport
    {
        private readonly Func<JArray, CancellationToken, ChatTurn> reply;
        public FakeTransport(Func<JArray, CancellationToken, ChatTurn> reply) => this.reply = reply;
        public Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken cancellationToken)
            => Task.FromResult(reply(messages, cancellationToken));
    }

    private sealed class FakeHands : IHandsBackend
    {
        public WorldSnapshot World = new WorldSnapshot();
        public int Moves;
        public int Captures;
        public int TransferLimit = int.MaxValue;
        public string Rejection;
        public bool ThrowOnMove;
        public bool Open;
        public FakeHands()
        {
            AddContainer("inventory", "物品栏"); AddContainer("counter", "柜台"); AddContainer("customer", "窗口");
            AddContainer("documents", "档案架"); AddContainer("mentor", "导师笔记", "documents"); AddContainer("trash", "垃圾桶");
            AddContainer("furnace", "熔炉", "inventory", "group"); AddContainer("input", "原料", "furnace", role: "input"); AddContainer("output", "成品", "furnace", role: "output");
            AddContainer("bag-inner", "背包内容", "inventory");
            AddContainer("fridge", "冰箱", "inventory", "group");
            AddContainer("fridge-power", "电池", "fridge", "slot", role: "power");
            AddItem("ore", "普通矿石", "inventory", 8); AddItem("note-a", "笔记", "mentor", 1); AddItem("note-b", "笔记", "mentor", 1);
            AddItem("bag", "背包", "inventory", 1); World.Items.Last().ContentContainerId = "bag-inner";
            AddItem("spare-battery", "电池", "inventory", 2);
            AddItem("battery", "电池", "fridge-power", 1); World.Items.Last().InUse = true;
        }
        private void AddContainer(string id, string name, string parent = null, string kind = "grid", string role = null)
        {
            var path = parent == null ? name : World.Containers.First(c => c.Id == parent).Path + " → " + name;
            World.Containers.Add(new ContainerInfo { Id = id, Name = name, Path = path, ParentId = parent, Kind = kind, Role = role });
        }
        private void AddItem(string id, string name, string parent, int quantity)
        {
            World.Items.Add(new ItemInfo { Id = id, Name = name, Identifier = name, ContainerId = parent,
                Path = World.Containers.First(c => c.Id == parent).Path + " → " + name, Quantity = quantity, Owned = true, Removable = true });
        }
        public WorldSnapshot Capture() { Captures++; return World; }
        public MoveCheck CheckMove(string itemId, string sourceId, string destinationId, int quantity)
            => new MoveCheck { Allowed = Rejection == null, Reason = Rejection, FirstSlotQuantity = quantity };
        public MoveOutcome Move(string itemId, string sourceId, string destinationId, int quantity)
        {
            Moves++;
            if (ThrowOnMove) throw new Exception("native error");
            int moved = Math.Min(quantity, TransferLimit);
            var item = World.Items.First(i => i.Id == itemId);
            if (moved == item.Quantity) item.ContainerId = destinationId;
            else item.Quantity -= moved;
            return new MoveOutcome { Moved = moved };
        }
        public void SetOpen(string containerId, bool open) => Open = open;
    }
}
