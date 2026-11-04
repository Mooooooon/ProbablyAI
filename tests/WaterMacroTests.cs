using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using ProbablyAI.Liquids;
using ProbablyAI.Machines;
using ProbablyAI.Macros;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunWaterMacroTests()
    {
        await Check("water macro processes multiple batches and records actual filtered receipts", async () =>
        {
            var world = new WaterMacroWorld { TankFilterLoss = 7 };
            var result = await RunWaterMacro(world, batches: 2);
            var data = Doc(result)["data"];
            Assert(result.Success && world.Fills == 2 && world.Pours == 2 && world.UvUses == 2 && world.TabletsUsed == 4,
                result.Message);
            Assert((int)data["completed_iterations"]["steps[1]"] == 2 &&
                data["reports"].All(report => (int)report["value"]["received_ul"] == 2999993 &&
                    (int)report["value"]["filtered_ul"] == 7) && world.Inspect(world.Id("tank")).VolumeUl == 5999986,
                "macro used nominal batch capacity instead of measured transfer");
        });

        await Check("water macro installs a missing filter and replaces a spent filter after filling", async () =>
        {
            var noFilter = new WaterMacroWorld();
            noFilter.Item("old_filter").ContainerId = "inventory";
            Assert((await RunWaterMacro(noFilter)).Success && noFilter.Hands.Moves == 1, "missing filter was not installed");

            var spent = new WaterMacroWorld("i-aabbccdd-");
            spent.Item("old_filter").Durability.Current = 1;
            var result = await RunWaterMacro(spent);
            Assert(result.Success && spent.Hands.Moves == 2 && spent.Pours == 3 &&
                spent.Item("old_filter").ContainerId == "inventory" && spent.Item("spare_filter").ContainerId == spent.Id("filter_slot") &&
                spent.Inspect(spent.Id("temporary")).VolumeUl == 0, result.Message);
        });

        await Check("water macro stops on unknown durability and missing replacement supplies", async () =>
        {
            var unknown = new WaterMacroWorld { LoseDurabilityOnFill = true };
            var result = await RunWaterMacro(unknown);
            Assert(result.Code == "macro_assertion" && result.Message.Contains("unknown filter durability") &&
                unknown.Fills == 1 && unknown.Pours == 0 && unknown.UvUses == 0 &&
                (int)Doc(result)["data"]["completed_iterations"]["steps[1]"] == 0, "unknown durability was treated as available");
            var spent = new WaterMacroWorld();
            spent.Item("old_filter").Durability.Current = 1;
            spent.Hands.World.Items.Remove(spent.Item("spare_filter"));
            var missing = await RunWaterMacro(spent);
            Assert(missing.Code == "macro_no_match" && spent.Fills == 1 && spent.Pours == 0 && spent.Hands.Moves == 0,
                "water was decanted before finding a replacement filter");
        });

        await Check("water macro stops before UV when tablets are missing and before pouring when the tank is too small", async () =>
        {
            var pills = new WaterMacroWorld();
            pills.Hands.World.Items.RemoveAll(item => item.Identifier == "water_tablet" && item.Id != pills.Id("pill1"));
            Assert((await RunWaterMacro(pills)).Code == "macro_no_match" && pills.UvUses == 0 && pills.TabletsUsed == 0 && pills.Pours == 0,
                "one tablet was consumed despite missing the second");
            var full = new WaterMacroWorld { TankInitialVolume = 17000001 };
            Assert((await RunWaterMacro(full)).Code == "target_too_small" && full.Pours == 0 &&
                full.Inspect(full.Id("pitcher")).VolumeUl == 3000000, "insufficient capacity caused a partial native pour");
        });

        await Check("water macro reports a partial native pour and keeps completed operations on stop", async () =>
        {
            var partial = new WaterMacroWorld { LimitTankPour = 1234567 };
            var outcome = await RunWaterMacro(partial, batches: 2);
            Assert(outcome.Code == "partial" && partial.Fills == 1 && partial.Pours == 1 &&
                (int)Doc(outcome)["data"]["failed_tool_data"]["received_ul"] == 1234567 &&
                (int)Doc(outcome)["data"]["completed_iterations"]["steps[1]"] == 0, "partial native effect was lost or retried");

            using var cancellation = new CancellationTokenSource();
            var world = new WaterMacroWorld { AfterFill = cancellation.Cancel };
            var stopped = await RunWaterMacro(world, token: cancellation.Token);
            Assert(stopped.Code == "macro_cancelled" && world.Fills == 1 && world.Pours == 0 && world.TabletsUsed == 0 &&
                world.Inspect(world.Id("pitcher")).VolumeUl == 3000000 &&
                Doc(stopped)["data"]["recent_operations"].Any(), "cancellation lost the recorded fill or allowed more actions");
        });
    }

    private static async Task<HandsResult> RunWaterMacro(WaterMacroWorld world, int batches = 1, CancellationToken token = default)
    {
        var registry = ToolRegistry.ForHands(new HandsService(world.Hands));
        new LiquidService(world).Register(registry);
        new ItemUseService(world).Register(registry);
        world.RegisterMachine(registry);
        var macros = TestMacros(registry);
        var entry = macros.Save(null, null, MacroDefinition.Parse(WaterRecipe().ToString()), false);
        return await macros.RunAsync(entry.Id, new JObject { ["batches"] = batches }, token);
    }

    private static JObject WaterRecipe()
    {
        static JObject Find(string identifier, string result, JToken container = null, bool includePinned = false)
        {
            var args = new JObject { ["query"] = identifier, ["owned_only"] = true, ["limit"] = 100 };
            if (container != null) args["container_id"] = container;
            if (includePinned) args["include_pinned"] = true;
            return MacroCall("find_items", args, result);
        }
        static JObject Select(string from, string binding, string mode, JObject where = null)
        {
            var step = new JObject { ["op"] = "select", ["from"] = Reference(from), ["as"] = binding, ["mode"] = mode };
            if (where != null) step["where"] = where;
            return step;
        }
        static JObject Comparison(string op, string field, JToken value)
            => new JObject { ["op"] = op, ["left"] = Reference(field), ["right"] = value };
        static JObject Exists(string name) => new JObject { ["op"] = "exists", ["value"] = Reference(name) };
        static JObject Require(JObject test, string message) => new JObject { ["op"] = "assert", ["test"] = test, ["message"] = message };
        static JObject Branch(JObject test, JObject[] yes, JObject[] no = null)
        {
            var step = new JObject { ["op"] = "if", ["test"] = test, ["then"] = new JArray(yes) };
            if (no != null) step["else"] = new JArray(no);
            return step;
        }
        static JObject ItemCall(string tool, string item, string binding = null)
            => MacroCall(tool, new JObject { ["item_id"] = Reference(item + ".id") }, binding);
        static JObject Slot(string binding) => MacroCall("list_container",
            new JObject { ["container_id"] = Reference("pitcher.content_container_id") }, binding);
        static JObject MoveFilter(string item, JToken destination) => MacroCall("move_item", new JObject
        {
            ["item_id"] = Reference(item + ".id"), ["source_container_id"] = Reference(item + ".container_id"),
            ["destination_container_id"] = destination, ["quantity"] = 1
        });
        static JObject NewFilter() => Select("loose.page.entries", "replacement", "first", new JObject
        {
            ["op"] = "all", ["tests"] = new JArray(Comparison("eq", "item.container_id", "inventory"),
                Exists("item.durability.current"), Comparison("gt", "item.durability.current", 0))
        });
        static JObject Pour(string from, string to, string binding = null) => MacroCall("pour_liquid", new JObject
        {
            ["source_item_id"] = Reference(from + ".id"), ["target_item_id"] = Reference(to + ".id"), ["require_full_transfer"] = true
        }, binding);
        static JObject Treat(string material, string binding) => MacroCall("use_item_on_item", new JObject
        {
            ["item_id"] = Reference(material + ".id"), ["target_item_id"] = Reference("pitcher.id")
        }, binding);
        static JObject Tablet(string binding, string differentFrom = null)
        {
            var single = Comparison("eq", "item.quantity", 1);
            return Select("tablets.page.entries", binding, "first", differentFrom == null ? single : new JObject
            {
                ["op"] = "all", ["tests"] = new JArray(single, Comparison("ne", "item.id", Reference(differentFrom + ".id")))
            });
        }

        var body = new JArray(
            Find("water_pitcher", "pitchers"),
            Select("pitchers.page.entries", "pitcher", "one", Comparison("eq", "item.identifier", "water_pitcher")),
            ItemCall("inspect_liquid", "pitcher", "pitcher_volume"),
            Require(Comparison("eq", "pitcher_volume.capacity_ul", 3000000), "expected 3000 mL pitcher"),
            Require(Comparison("eq", "pitcher_volume.volume_ul", 0), "pitcher is not empty"),
            Find("water_purifier", "machines", includePinned: true),
            Select("machines.page.entries", "purifier", "one", Comparison("eq", "item.identifier", "water_purifier")),
            ItemCall("inspect_machine", "purifier", "machine"),
            Select("machine.compartments", "tank_slot", "one", Comparison("eq", "item.role", "container")),
            Find("water_tank", "tanks", Reference("tank_slot.id"), includePinned: true),
            Select("tanks.page.entries", "tank", "one", Comparison("eq", "item.identifier", "water_tank")),
            Slot("initial_slot"),
            Select("initial_slot.items.entries", "mounted", "optional_one"),
            Branch(Exists("mounted"), new[]
            {
                Require(Exists("mounted.durability.current"), "unknown filter durability"),
                Branch(Comparison("eq", "mounted.durability.current", 0), new[]
                {
                    Find("water_filter", "loose"), NewFilter(),
                    MoveFilter("mounted", "inventory"), MoveFilter("replacement", Reference("pitcher.content_container_id"))
                })
            }, new[]
            {
                Find("water_filter", "loose"), NewFilter(), MoveFilter("replacement", Reference("pitcher.content_container_id"))
            }),
            ItemCall("fill_liquid", "pitcher"),
            Slot("slot_after_fill"),
            Select("slot_after_fill.items.entries", "after_fill", "one"),
            Require(Exists("after_fill.durability.current"), "unknown filter durability"),
            Branch(Comparison("eq", "after_fill.durability.current", 0), new[]
            {
                Find("water_jug", "jugs"), Select("jugs.page.entries", "temporary", "one", Comparison("eq", "item.identifier", "water_jug")),
                ItemCall("inspect_liquid", "temporary", "temporary_volume"),
                Require(Comparison("eq", "temporary_volume.volume_ul", 0), "temporary jug is not empty"),
                Find("water_filter", "loose"), NewFilter(),
                Pour("pitcher", "temporary"), MoveFilter("after_fill", "inventory"),
                MoveFilter("replacement", Reference("pitcher.content_container_id")), Pour("temporary", "pitcher")
            }),
            Find("uv_filter", "lamps"),
            Select("lamps.page.entries", "lamp", "first", Comparison("eq", "item.identifier", "uv_filter")),
            Find("water_tablet", "tablets"),
            Tablet("pill1"), Tablet("pill2", "pill1"),
            Treat("lamp", "uv"),
            Treat("pill1", "first_pill"),
            Require(Comparison("eq", "first_pill.source.consumed", true), "first tablet was not consumed"),
            Treat("pill2", "second_pill"),
            Require(Comparison("eq", "second_pill.source.consumed", true), "second tablet was not consumed"),
            Pour("pitcher", "tank", "transfer"),
            new JObject { ["op"] = "report", ["name"] = "batch volume",
                ["value"] = new JObject { ["received_ul"] = Reference("transfer.received_ul"), ["filtered_ul"] = Reference("transfer.filtered_ul") } });
        var definition = Definition(new JObject { ["op"] = "repeat", ["times"] = Reference("inputs.batches"), ["steps"] = body });
        definition["inputs"] = new JObject { ["batches"] = new JObject { ["type"] = "integer", ["min"] = 1, ["max"] = 20 } };
        return definition;
    }

    private sealed class WaterMacroWorld : ILiquidBackend, IItemUseBackend
    {
        internal FakeHands Hands { get; } = new FakeHands();
        private readonly string idPrefix;
        private readonly Dictionary<string, int> volumes = new Dictionary<string, int>();
        internal int TankFilterLoss, LimitTankPour = int.MaxValue, Fills, Pours, UvUses, TabletsUsed;
        internal bool LoseDurabilityOnFill;
        internal Action AfterFill;
        internal int TankInitialVolume { set => volumes[Id("tank")] = value; }

        internal WaterMacroWorld(string idPrefix = "i-12345678-")
        {
            this.idPrefix = idPrefix;
            Add("pitcher", "water_pitcher", "inventory").ContentContainerId = Id("filter_slot");
            Add("temporary", "water_jug", "inventory");
            Add("purifier", "water_purifier", "inventory").ContentContainerId = Id("purifier_root");
            Item("purifier").Pinned = true;
            Add("tank", "water_tank", Id("tank_slot"));
            Add("old_filter", "water_filter", Id("filter_slot")).Durability = FilterDurability();
            Add("spare_filter", "water_filter_adv", "inventory").Durability = FilterDurability();
            Add("uv", "uv_filter", "inventory");
            for (int i = 1; i <= 4; i++) Add("pill" + i, "water_tablet", "inventory");

            AddSlot("filter_slot", "inventory", "pitcher");
            AddSlot("purifier_root", "inventory", "purifier");
            AddSlot("tank_slot", Id("purifier_root"), "purifier", "container");
            foreach (string item in new[] { "pitcher", "temporary", "tank" }) volumes.Add(Id(item), 0);
        }

        internal void RegisterMachine(ToolRegistry registry)
            => new MachineService(id =>
            {
                Assert(id == Id("purifier"), "wrong purifier selected");
                return HandsResult.Ok(new { compartments = Hands.World.Containers.Where(c => c.Role == "container").ToArray() });
            }, (_, _, _, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()).Register(registry);

        private ItemInfo Add(string name, string identifier, string container)
        {
            var item = new ItemInfo { Id = Id(name), Name = name, Identifier = identifier, ContainerId = container,
                Owned = true, Removable = true, Quantity = 1 };
            Hands.World.Items.Add(item);
            return item;
        }

        private void AddSlot(string name, string parent, string owner, string role = null) => Hands.World.Containers.Add(
            new ContainerInfo { Id = Id(name), Name = name, ParentId = parent, OwnerItemId = Id(owner), Kind = "slot", Role = role });

        private static ItemDurability FilterDurability() => new ItemDurability { Current = 100, Maximum = 400, AssessmentSource = "tested" };
        internal string Id(string name)
        {
            int index = Array.IndexOf(new[] { "pitcher", "temporary", "purifier", "tank", "old_filter", "spare_filter", "uv",
                "pill1", "pill2", "pill3", "pill4", "filter_slot", "purifier_root", "tank_slot" }, name);
            if (index < 0) throw new ArgumentException(name);
            return idPrefix + (index + 1);
        }
        internal ItemInfo Item(string name) => Hands.World.Items.Single(item => item.Id == Id(name));

        public LiquidReading Inspect(string itemId) => new LiquidReading
        {
            Item = Hands.World.Items.Single(item => item.Id == itemId), Container = true, Supported = true, Owned = true,
            VolumeUl = volumes[itemId], CapacityUl = itemId == Id("tank") ? 20000000 : itemId == Id("temporary") ? 6000000 : 3000000
        };

        public LiquidChange Fill(string itemId)
        {
            var before = Inspect(itemId);
            var filter = Hands.World.Items.Single(item => item.ContainerId == Id("filter_slot"));
            Assert(filter.Durability?.Current > 0, "filled without working filter");
            filter.Durability.Current--;
            if (LoseDurabilityOnFill) filter.Durability = null;
            volumes[itemId] = before.CapacityUl;
            Fills++;
            AfterFill?.Invoke();
            return LiquidChange.MeasureFill(before, Inspect(itemId));
        }

        public LiquidChange Pour(string sourceItemId, string targetItemId, bool requireFullTransfer = false)
        {
            var source = Inspect(sourceItemId);
            var target = Inspect(targetItemId);
            var error = LiquidRules.ValidatePour(source, target, requireFullTransfer);
            if (error != null) throw new HandsException(error.Code, error.Message);
            int moved = Math.Min(source.VolumeUl, target.FreeUl);
            if (targetItemId == Id("tank")) moved = Math.Min(moved, LimitTankPour);
            volumes[sourceItemId] -= moved;
            volumes[targetItemId] += targetItemId == Id("tank") ? moved - TankFilterLoss : moved;
            Pours++;
            return LiquidChange.MeasurePour(source, Inspect(sourceItemId), target, Inspect(targetItemId));
        }

        public ItemUseChange Use(string sourceItemId, string targetItemId)
        {
            var material = Hands.World.Items.Single(item => item.Id == sourceItemId);
            var source = new ItemUseReading { Item = material, Kind = material.Identifier, Quantity = material.Quantity };
            var reading = Inspect(targetItemId);
            var target = new ItemUseReading { Item = reading.Item, LiquidContainer = true, VolumeUl = reading.VolumeUl, CapacityUl = reading.CapacityUl };
            bool pill = material.Identifier == "water_tablet";
            if (pill) { TabletsUsed++; Hands.World.Items.Remove(material); }
            else { Assert(material.Identifier == "uv_filter", "unexpected treatment"); UvUses++; }
            return new ItemUseChange { SourceBefore = source, SourceAfter = pill ? null : source, SourceConsumed = pill, TargetBefore = target, TargetAfter = target };
        }

        public LiquidChange Dump(string itemId) => throw new InvalidOperationException();
        public ItemUseChange Activate(string itemId) => throw new InvalidOperationException();
    }
}
