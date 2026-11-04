using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Liquids;
using ProbablyAI.Localization;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunLiquidTests()
    {
        await Check("liquid tools share the registry, change state and expose strict schemas", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new LiquidService(new FakeLiquid()).Register(registry);
            foreach (string name in LiquidNames)
                Assert(registry.Contains(name) && registry.ChangesState(name), "liquid tool missing or read-only: " + name);
            Assert(registry.Definitions().Count(d => LiquidNames.Contains((string)d["function"]["name"])) == 3, "liquid tool count");
            var pour = registry.Definitions().Single(d => (string)d["function"]["name"] == "pour_liquid");
            var schema = pour["function"]["parameters"];
            Assert(!(bool)schema["additionalProperties"] &&
                JToken.DeepEquals(schema["required"], new JArray("source_item_id", "target_item_id")), "unsafe pour schema");
            var dump = registry.Definitions().Single(d => (string)d["function"]["name"] == "dump_liquid");
            Assert(JToken.DeepEquals(dump["function"]["parameters"]["required"], new JArray("item_id")), "unsafe dump schema");
        });
        await Check("liquid tools reject malformed or unrelated requests before touching game state", () =>
        {
            var backend = new FakeLiquid(); var service = new LiquidService(backend);
            foreach (var request in new[]
            {
                new object[] { "pour_liquid", null }, new object[] { "pour_liquid", new JObject() },
                new object[] { "pour_liquid", new JObject { ["source_item_id"] = "a" } },
                new object[] { "pour_liquid", new JObject { ["source_item_id"] = "a", ["target_item_id"] = 2 } },
                new object[] { "pour_liquid", new JObject { ["source_item_id"] = "a", ["target_item_id"] = "b", ["quantity"] = 1 } },
                new object[] { "dump_liquid", new JObject { ["source_item_id"] = "a" } },
                new object[] { "fill_liquid", new JObject { ["item_id"] = " ", ["extra"] = true } },
                new object[] { "fill_liquid", new JObject { ["item_id"] = new string('x', 201) } }
            })
                Assert(service.Execute((string)request[0], (JObject)request[1]).Code == "invalid_arguments", "invalid liquid request accepted");
            var same = service.Execute("pour_liquid", new JObject { ["source_item_id"] = "same", ["target_item_id"] = "same" });
            Assert(same.Code == "same_item" && backend.Pours + backend.Dumps + backend.Fills == 0, "same-container pour reached the game");
            Assert(service.Execute("boil_liquid", new JObject()).Code == "unknown_tool" && backend.Pours == 0, "unknown liquid tool executed");
        });
        await Check("pour reports actual moved and received volumes and both remaining levels", () =>
        {
            var backend = new FakeLiquid
            {
                PourResult = LiquidChange.MeasurePour(
                    Liquid("bottle", 1000000, 500000), Liquid("bottle", 1000000, 0),
                    Liquid("jug", 2000000, 0), Liquid("jug", 2000000, 500000))
            };
            var result = new LiquidService(backend).Execute("pour_liquid",
                new JObject { ["source_item_id"] = "bottle", ["target_item_id"] = "jug" });
            var data = Doc(result)["data"];
            Assert(result.Success && result.Code == "ok" && backend.Pours == 1, "pour not delegated or reported as success");
            Assert((string)data["direction"] == "source_to_target" && (double)data["moved_ml"] == 500 &&
                (double)data["received_ml"] == 500 && (double)data["source"]["volume_ml"] == 0 &&
                (double)data["target"]["volume_ml"] == 500 && data["filtered_ml"] == null, "pour volumes lost");
            Assert(result.Message.Contains("倒出 500 ml") && result.Message.Contains("剩余 0/1000 ml"), "pour message lacks actual amounts");
        });
        await Check("filtered pour reports the difference instead of claiming all liquid arrived", () =>
        {
            var backend = new FakeLiquid
            {
                PourResult = LiquidChange.MeasurePour(
                    Liquid("dirty", 1000000, 500000), Liquid("dirty", 1000000, 0),
                    Liquid("pitcher", 2000000, 0), Liquid("pitcher", 2000000, 400000))
            };
            var result = new LiquidService(backend).Execute("pour_liquid",
                new JObject { ["source_item_id"] = "dirty", ["target_item_id"] = "pitcher" });
            var data = Doc(result)["data"];
            Assert(result.Success && (double)data["moved_ml"] == 500 && (double)data["received_ml"] == 400 &&
                (double)data["filtered_ml"] == 100, "filtered amount lost");
            Assert(result.Message.Contains("目标接收 400 ml") && result.Message.Contains("过滤器"), "filter explanation missing");
        });
        await Check("empty dropper used on a container is reported as drawing liquid", () =>
        {
            var backend = new FakeLiquid
            {
                PourResult = LiquidChange.MeasurePour(
                    Liquid("dropper", 1000, 0), Liquid("dropper", 1000, 300),
                    Liquid("bottle", 1000000, 500000), Liquid("bottle", 1000000, 200000))
            };
            var result = new LiquidService(backend).Execute("pour_liquid",
                new JObject { ["source_item_id"] = "dropper", ["target_item_id"] = "bottle" });
            var data = Doc(result)["data"];
            Assert(result.Success && (string)data["direction"] == "target_to_source" &&
                (double)data["moved_ml"] == 300, "dropper draw direction lost");
            Assert(result.Message.Contains("吸取"), "draw message missing");
        });
        await Check("no-op pour is a failure and never claims a completed transfer", () =>
        {
            var backend = new FakeLiquid
            {
                PourResult = LiquidChange.MeasurePour(
                    Liquid("bottle", 1000000, 500000), Liquid("bottle", 1000000, 500000),
                    Liquid("jug", 2000000, 0), Liquid("jug", 2000000, 0))
            };
            var result = new LiquidService(backend).Execute("pour_liquid",
                new JObject { ["source_item_id"] = "bottle", ["target_item_id"] = "jug" });
            Assert(!result.Success && result.Code == "not_poured" && (double)Doc(result)["data"]["moved_ml"] == 0, "no-op pour reported as success");
            Assert(result.Message.Contains("没有液体移动"), "no-op explanation missing");
        });
        await Check("native liquid refusals and invalid native amounts propagate as failures", () =>
        {
            var backend = new FakeLiquid();
            var service = new LiquidService(backend);
            foreach (string code in new[] { "item_not_found", "target_full", "liquid_action_rejected", "tap_locked" })
            {
                backend.Failure = new HandsException(code, "测试原因");
                var result = service.Execute("fill_liquid", new JObject { ["item_id"] = "bottle" });
                Assert(!result.Success && result.Code == code, "native liquid refusal lost: " + code);
            }
            backend.Failure = null;
            backend.FillResult = LiquidChange.MeasureFill(Liquid("bottle", 1000000, 0), Liquid("bottle", 1000000, 2000000));
            Assert(service.Execute("fill_liquid", new JObject { ["item_id"] = "bottle" }).Code == "invalid_native_result", "over-capacity fill accepted");
        });
        await Check("liquid rules enforce container type, ownership, capacity and finished-wine space", () =>
        {
            var source = Liquid("bottle", 1000000, 500000);
            var target = Liquid("jug", 2000000, 0);
            Assert(LiquidRules.ValidatePour(source, target) == null, "valid pour rejected");
            Assert(LiquidRules.ValidatePour(Liquid("stone", 0, 0, container: false), target).Code == "not_liquid_container", "non-container accepted");
            Assert(LiquidRules.ValidatePour(Liquid("legacy", 0, 0, supported: false), target).Code == "unsupported_container", "legacy container accepted");
            Assert(LiquidRules.ValidatePour(Liquid("guest", 1000000, 500000, owned: false), target).Code == "not_owned", "guest container accepted");
            Assert(LiquidRules.ValidatePour(source, Liquid("bottle", 1000000, 0)).Code == "same_item", "self pour accepted");
            Assert(LiquidRules.ValidatePour(Liquid("empty", 1000000, 0), target).Code == "source_empty", "empty source accepted");
            Assert(LiquidRules.ValidatePour(source, Liquid("full", 1000000, 1000000)).Code == "target_full", "full target accepted");
            Assert(LiquidRules.ValidatePour(Liquid("wine", 750000, 750000, wineReady: true), Liquid("small", 1000000, 500000)).Code == "target_too_small", "finished wine accepted into too little space");
            Assert(LiquidRules.ValidateDump(Liquid("empty", 1000000, 0)).Code == "source_empty", "empty dump accepted");
            Assert(LiquidRules.ValidateFill(Liquid("full", 1000000, 1000000)).Code == "target_full", "full faucet fill accepted");
            Assert(LiquidRules.ValidateDump(source) == null && LiquidRules.ValidateFill(target) == null, "valid dump/fill rejected");
        });
        await Check("dump and fill report the actual volume change", () =>
        {
            var backend = new FakeLiquid
            {
                DumpResult = LiquidChange.MeasureDump(Liquid("bottle", 1000000, 700000), Liquid("bottle", 1000000, 0)),
                FillResult = LiquidChange.MeasureFill(Liquid("jug", 2000000, 0), Liquid("jug", 2000000, 1500000))
            };
            var service = new LiquidService(backend);
            var dump = service.Execute("dump_liquid", new JObject { ["item_id"] = "bottle" });
            var fill = service.Execute("fill_liquid", new JObject { ["item_id"] = "jug" });
            Assert(dump.Success && dump.Code == "ok" && (double)Doc(dump)["data"]["emptied_ml"] == 700 &&
                (double)Doc(dump)["data"]["remaining_ml"] == 0 && dump.Message.Contains("排水口"), "dump result lost");
            Assert(fill.Success && fill.Code == "ok" && (double)Doc(fill)["data"]["added_ml"] == 1500 &&
                (double)Doc(fill)["data"]["volume_ml"] == 1500 && fill.Message.Contains("水龙头"), "fill result lost");
            backend.DumpResult = LiquidChange.MeasureDump(Liquid("bottle", 1000000, 0), Liquid("bottle", 1000000, 0));
            backend.FillResult = LiquidChange.MeasureFill(Liquid("jug", 2000000, 2000000), Liquid("jug", 2000000, 2000000));
            Assert(service.Execute("dump_liquid", new JObject { ["item_id"] = "bottle" }).Code == "not_dumped", "empty dump reported as success");
            Assert(service.Execute("fill_liquid", new JObject { ["item_id"] = "jug" }).Code == "not_filled", "full fill reported as success");
            Assert(backend.Dumps == 2 && backend.Fills == 2, "dump/fill not delegated");
        });
        await Check("agent pours liquid through one native action and reports it", async () =>
        {
            var backend = new FakeLiquid
            {
                PourResult = LiquidChange.MeasurePour(
                    Liquid("bottle", 1000000, 500000), Liquid("bottle", 1000000, 0),
                    Liquid("jug", 2000000, 0), Liquid("jug", 2000000, 500000))
            };
            var registry = new ToolRegistry();
            new LiquidService(backend).Register(registry);
            int round = 0;
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                if (round++ == 0)
                    return Call("pour-1", "pour_liquid", new JObject { ["source_item_id"] = "bottle", ["target_item_id"] = "jug" });
                var reply = JObject.Parse((string)messages.Last["content"]);
                Assert((string)reply["code"] == "ok" && (double)reply["data"]["moved_ml"] == 500, "tool reply lacks actual pour");
                return new ChatTurn { Text = "水已经倒进水壶了。" };
            }), registry);
            var result = await agent.RunAsync(new ConnectionOptions(), "把瓶子里的水倒进水壶", null, default);
            Assert(result.ToolCalls == 1 && result.Operations.Count == 1 && backend.Pours == 1, "pour did not use one operation");
        });
        await Check("liquid messages and schemas follow the game language", () =>
        {
            Text.UseLocale("en");
            try
            {
                var backend = new FakeLiquid
                {
                    FillResult = LiquidChange.MeasureFill(Liquid("jug", 2000000, 0), Liquid("jug", 2000000, 1500000))
                };
                var result = new LiquidService(backend).Execute("fill_liquid", new JObject { ["item_id"] = "jug" });
                Assert(result.Success && result.Message.StartsWith("Water from the faucet filled jug: 1500 ml added"), "English liquid message missing");
                var registry = new ToolRegistry();
                new LiquidService(backend).Register(registry);
                Assert(!registry.Definitions().ToString().Contains("液体"), "English liquid schema left untranslated");
            }
            finally { Text.UseLocale("zh"); }
        });
    }

    private static readonly string[] LiquidNames = { "pour_liquid", "dump_liquid", "fill_liquid" };

    private static LiquidReading Liquid(string id, int capacityUl, int volumeUl, bool container = true,
        bool supported = true, bool owned = true, bool wineReady = false)
        => new LiquidReading
        {
            Item = new ItemInfo { Id = id, Name = id, Path = "物品栏 → " + id, Quantity = 1, Owned = owned },
            Container = container, Supported = supported && container, Owned = owned,
            VolumeUl = volumeUl, CapacityUl = capacityUl, WineReady = wineReady
        };

    private sealed class FakeLiquid : ILiquidBackend
    {
        public LiquidChange PourResult;
        public LiquidChange DumpResult;
        public LiquidChange FillResult;
        public HandsException Failure;
        public int Pours, Dumps, Fills;

        public LiquidReading Inspect(string itemId) => PourResult?.SourceBefore;

        public LiquidChange Pour(string sourceItemId, string targetItemId, bool requireFullTransfer = false)
        {
            Pours++;
            if (Failure != null) throw Failure;
            return PourResult;
        }

        public LiquidChange Dump(string itemId)
        {
            Dumps++;
            if (Failure != null) throw Failure;
            return DumpResult;
        }

        public LiquidChange Fill(string itemId)
        {
            Fills++;
            if (Failure != null) throw Failure;
            return FillResult;
        }
    }
}
