using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Documents;
using ProbablyAI.Hands;
using ProbablyAI.Inspection;
using ProbablyAI.Items;
using ProbablyAI.Liquids;
using ProbablyAI.Localization;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunItemUseTests()
    {
        await Check("item-use tool shares the registry, changes state and exposes a strict schema", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new ItemUseService(new FakeItemUse()).Register(registry);
            Assert(registry.Contains("use_item_on_item") && registry.ChangesState("use_item_on_item"), "item-use tool missing or read-only");
            var definition = registry.Definitions().Single(d => (string)d["function"]["name"] == "use_item_on_item");
            var schema = definition["function"]["parameters"];
            Assert(!(bool)schema["additionalProperties"] &&
                JToken.DeepEquals(schema["required"], new JArray("item_id", "target_item_id")), "unsafe item-use schema");
            Assert(definition.ToString().Contains("water_tablet") && definition.ToString().Contains("water_ration"), "supported item scope missing");
        });
        await Check("item-use rejects malformed, extra and self-targeted requests before touching game state", () =>
        {
            var backend = new FakeItemUse(); var service = new ItemUseService(backend);
            foreach (var arguments in new JObject[]
            {
                null, new JObject(), new JObject { ["item_id"] = "tablet" },
                new JObject { ["item_id"] = "tablet", ["target_item_id"] = 2 },
                new JObject { ["item_id"] = "tablet", ["target_item_id"] = "water", ["quantity"] = 1 },
                new JObject { ["item_id"] = " ", ["target_item_id"] = "water" },
                new JObject { ["item_id"] = new string('x', 201), ["target_item_id"] = "water" }
            })
                Assert(service.Execute("use_item_on_item", arguments).Code == "invalid_arguments", "invalid item use accepted");
            Assert(service.Execute("use_item_on_item", new JObject { ["item_id"] = "same", ["target_item_id"] = "same" }).Code == "same_item" &&
                backend.Uses == 0, "self use reached the game");
            Assert(service.Execute("use_weapon", new JObject()).Code == "unknown_tool" && backend.Uses == 0, "unknown item-use tool executed");
        });
        await Check("purification tablet reports native consumption and the actual target volume", () =>
        {
            var backend = new FakeItemUse
            {
                Result = UseResult(
                    Source("water_tablet", ItemUseRules.WaterTablet, quantity: 3, afterQuantity: null),
                    Target("bottle", volumeUl: 100000, capacityUl: 1000000),
                    Target("bottle", volumeUl: 115000, capacityUl: 1000000),
                    consumed: true, changed: true)
            };
            var result = new ItemUseService(backend).Execute("use_item_on_item",
                new JObject { ["item_id"] = "water_tablet", ["target_item_id"] = "bottle" });
            var data = Doc(result)["data"];
            Assert(result.Success && result.Code == "ok" && backend.Uses == 1, "tablet use not reported");
            Assert((string)data["effect"] == "water_tablet" && (bool)data["source"]["consumed"] &&
                (double)data["target"]["volume_ml"] == 115 && (double)data["target"]["capacity_ml"] == 1000, "tablet result lost");
            Assert(result.Message.Contains("净水药片") && result.Message.Contains("消耗") &&
                result.Message.Contains("115/1000 ml"), "tablet message lacks actual result");
        });
        await Check("UV filter and water ration use the same native action with their own results", () =>
        {
            var backend = new FakeItemUse
            {
                Result = UseResult(
                    Source("uv_filter", ItemUseRules.UVFilter, quantity: 1, afterQuantity: 1),
                    Target("bottle", volumeUl: 500000, capacityUl: 1000000),
                    Target("bottle", volumeUl: 500000, capacityUl: 1000000),
                    consumed: false, changed: true)
            };
            var service = new ItemUseService(backend);
            var uv = service.Execute("use_item_on_item", new JObject { ["item_id"] = "uv_filter", ["target_item_id"] = "bottle" });
            Assert(uv.Success && (bool)Doc(uv)["data"]["source"]["consumed"] == false && uv.Message.Contains("仍保留"), "UV filter result lost");
            backend.Result = UseResult(
                Source("water_ration", ItemUseRules.WaterRation, quantity: 1, afterQuantity: null),
                Target("jug", volumeUl: 0, capacityUl: 2000000, hasWater: false),
                Target("jug", volumeUl: 750000, capacityUl: 2000000, hasWater: true),
                consumed: true, changed: true);
            var ration = service.Execute("use_item_on_item", new JObject { ["item_id"] = "water_ration", ["target_item_id"] = "jug" });
            Assert(ration.Success && (double)Doc(ration)["data"]["target"]["volume_ml"] == 750 && ration.Message.Contains("水配给"), "water ration result lost");
        });
        await Check("a no-op item use is a failure and native refusals propagate", () =>
        {
            var backend = new FakeItemUse
            {
                Result = UseResult(
                    Source("uv_filter", ItemUseRules.UVFilter, quantity: 1, afterQuantity: 1),
                    Target("bottle", volumeUl: 500000, capacityUl: 1000000),
                    Target("bottle", volumeUl: 500000, capacityUl: 1000000),
                    consumed: false, changed: false)
            };
            var service = new ItemUseService(backend);
            var noop = service.Execute("use_item_on_item", new JObject { ["item_id"] = "uv_filter", ["target_item_id"] = "bottle" });
            Assert(!noop.Success && noop.Code == "not_used" && noop.Message.Contains("没有发生物品使用"), "no-op item use claimed success");
            foreach (string code in new[] { "item_not_found", "target_empty", "wrong_target", "item_use_rejected" })
            {
                backend.Failure = new HandsException(code, "测试原因");
                Assert(service.Execute("use_item_on_item", new JObject { ["item_id"] = "uv_filter", ["target_item_id"] = "bottle" }).Code == code,
                    "native item-use refusal lost: " + code);
            }
        });
        await Check("item-use rules enforce supported items, ownership, containers and water state", () =>
        {
            var tablet = Source("water_tablet", ItemUseRules.WaterTablet);
            var water = Target("bottle", 500000);
            Assert(ItemUseRules.Validate(tablet, water) == null, "valid tablet use rejected");
            Assert(ItemUseRules.Validate(Source("gun", null), water).Code == "unsupported_item", "unsupported source accepted");
            Assert(ItemUseRules.Validate(Source("tablet", ItemUseRules.WaterTablet, owned: false), water).Code == "not_owned", "guest tablet accepted");
            Assert(ItemUseRules.Validate(tablet, Target("bottle", 500000, owned: false)).Code == "not_owned", "guest target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("bottle", 500000, liquid: false)).Code == "target_not_liquid", "non-container target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("bottle", 500000, supported: false)).Code == "unsupported_container", "legacy target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("bottle", 500000, locked: true)).Code == "container_locked", "locked target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("wine", 500000, wine: true)).Code == "wrong_target", "wine target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("chem", 500000, chemical: true)).Code == "wrong_target", "chemical target accepted");
            Assert(ItemUseRules.Validate(tablet, Target("empty", 0, hasWater: false)).Code == "target_empty", "empty tablet target accepted");
            Assert(ItemUseRules.Validate(Source("uv", ItemUseRules.UVFilter), Target("empty", 0, hasWater: false)).Code == "target_empty", "empty UV target accepted");
            Assert(ItemUseRules.Validate(Source("ration", ItemUseRules.WaterRation, required: 750000), Target("empty", 0, hasWater: false)) == null, "empty ration target rejected");
            Assert(ItemUseRules.Validate(Source("ration", ItemUseRules.WaterRation, required: 750000), Target("small", 900000, capacityUl: 1000000)).Code == "target_too_small", "ration overflow accepted");
            Assert(ItemUseRules.Validate(tablet, Target("full", 1000000, capacityUl: 1000000)) == null, "full tablet target rejected");
        });
        await Check("agent uses a tablet on water through one native action", async () =>
        {
            var backend = new FakeItemUse
            {
                Result = UseResult(
                    Source("water_tablet", ItemUseRules.WaterTablet, quantity: 1, afterQuantity: null),
                    Target("bottle", 500000), Target("bottle", 515000), consumed: true, changed: true)
            };
            var registry = new ToolRegistry();
            new ItemUseService(backend).Register(registry);
            int round = 0;
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                if (round++ == 0)
                    return Call("use-1", "use_item_on_item", new JObject { ["item_id"] = "water_tablet", ["target_item_id"] = "bottle" });
                var reply = JObject.Parse((string)messages.Last["content"]);
                Assert((string)reply["code"] == "ok" && (string)reply["data"]["effect"] == "water_tablet", "tool reply lacks item use");
                return new ChatTurn { Text = "药片已经放进水里了。" };
            }), registry);
            var result = await agent.RunAsync(new ConnectionOptions(), "对着水瓶使用净水药片", null, default);
            Assert(result.ToolCalls == 1 && result.Operations.Count == 1 && backend.Uses == 1, "item use needed extra calls");
        });
        await Check("item-use messages and schemas follow the game language", () =>
        {
            Text.UseLocale("en");
            try
            {
                var backend = new FakeItemUse
                {
                    Result = UseResult(
                        Source("water_tablet", ItemUseRules.WaterTablet, quantity: 1, afterQuantity: null),
                        Target("bottle", 500000), Target("bottle", 515000), consumed: true, changed: true)
                };
                var result = new ItemUseService(backend).Execute("use_item_on_item",
                    new JObject { ["item_id"] = "water_tablet", ["target_item_id"] = "bottle" });
                Assert(result.Success && result.Message.StartsWith("bottle received water_tablet"), "English item-use message missing");
                var registry = new ToolRegistry();
                new ItemUseService(backend).Register(registry);
                Assert(!registry.Definitions().ToString().Contains("物品使用"), "English item-use schema left untranslated");
            }
            finally { Text.UseLocale("zh"); }
        });
    }

    private static ItemUseReading Source(string id, string kind, bool owned = true, int quantity = 1,
        int? afterQuantity = null, bool locked = false, int required = 0)
        => new ItemUseReading
        {
            Item = new ItemInfo { Id = id, Name = id, Path = "物品栏 → " + id, Quantity = quantity, Owned = owned },
            Kind = kind, Owned = owned, Locked = locked, Quantity = quantity, RequiredUl = required
        };

    private static ItemUseReading Target(string id, int volumeUl, int capacityUl = 1000000, bool owned = true,
        bool liquid = true, bool supported = true, bool locked = false, bool wine = false,
        bool chemical = false, bool hasWater = true)
        => new ItemUseReading
        {
            Item = new ItemInfo { Id = id, Name = id, Path = "物品栏 → " + id, Quantity = 1, Owned = owned },
            Owned = owned, Locked = locked, LiquidContainer = liquid, SupportedLiquid = supported,
            Wine = wine, ChemicalProduct = chemical, HasWater = hasWater,
            VolumeUl = volumeUl, CapacityUl = capacityUl
        };

    private static ItemUseChange UseResult(ItemUseReading sourceBefore, ItemUseReading targetBefore,
        ItemUseReading targetAfter, bool consumed, bool changed)
        => new ItemUseChange
        {
            SourceBefore = sourceBefore,
            SourceAfter = consumed ? null : sourceBefore,
            TargetBefore = targetBefore, TargetAfter = targetAfter,
            SourceConsumed = consumed, LiquidChanged = changed
        };

    private sealed class FakeItemUse : IItemUseBackend
    {
        public ItemUseChange Result;
        public HandsException Failure;
        public int Uses;

        public ItemUseChange Use(string sourceItemId, string targetItemId)
        {
            Uses++;
            if (Failure != null) throw Failure;
            return Result;
        }
    }
}


