using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using ProbablyAI.Localization;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunItemUseTests()
    {
        await Check("item-use tools share the registry, change state and expose strict schemas", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new ItemUseService(new FakeItemUse()).Register(registry);
            foreach (string name in new[] { "use_item_on_item", "activate_item" })
            {
                Assert(registry.Contains(name) && registry.ChangesState(name), "item-use tool missing or read-only: " + name);
                var definition = registry.Definitions().Single(d => (string)d["function"]["name"] == name);
                Assert(!(bool)definition["function"]["parameters"]["additionalProperties"], "unsafe item-use schema");
            }
            var use = registry.Definitions().Single(d => (string)d["function"]["name"] == "use_item_on_item");
            Assert(JToken.DeepEquals(use["function"]["parameters"]["required"], new JArray("item_id", "target_item_id")), "use schema changed");
        });
        await Check("item-use rejects malformed, extra and self-targeted requests before touching game state", () =>
        {
            var backend = new FakeItemUse(); var service = new ItemUseService(backend);
            foreach (var arguments in new JObject[]
            {
                null, new JObject(), new JObject { ["item_id"] = "item" },
                new JObject { ["item_id"] = "item", ["target_item_id"] = 2 },
                new JObject { ["item_id"] = "item", ["target_item_id"] = "target", ["quantity"] = 1 },
                new JObject { ["item_id"] = " ", ["target_item_id"] = "target" },
                new JObject { ["item_id"] = new string('x', 201), ["target_item_id"] = "target" }
            })
                Assert(service.Execute("use_item_on_item", arguments).Code == "invalid_arguments", "invalid item use accepted");
            Assert(service.Execute("activate_item", new JObject { ["item_id"] = "item", ["target_item_id"] = "target" }).Code == "invalid_arguments", "activation accepted a target");
            Assert(service.Execute("use_item_on_item", new JObject { ["item_id"] = "same", ["target_item_id"] = "same" }).Code == "same_item" &&
                backend.Uses == 0, "self use reached the game");
            Assert(service.Execute("use_weapon", new JObject()).Code == "unknown_tool" && backend.Uses == 0, "unknown item-use tool executed");
        });
        await Check("generic item-on-item use reports native acceptance and observed changes", () =>
        {
            var backend = new FakeItemUse
            {
                UseResult = UseResult(
                    Source("water_test_strip", null, 1),
                    Source("water_test_strip", null, 1, "用过的试纸"),
                    Target("bottle", 500000), Target("bottle", 500000),
                    consumed: false, changed: true)
            };
            var result = new ItemUseService(backend).Execute("use_item_on_item",
                new JObject { ["item_id"] = "water_test_strip", ["target_item_id"] = "bottle" });
            var data = Doc(result)["data"];
            Assert(result.Success && result.Code == "ok" && (string)data["effect"] == "item" &&
                (bool)data["changed"] && !(bool)data["source"]["consumed"], "generic item use result lost");
            Assert(result.Message.Contains("原生操作已执行") && result.Message.Contains("如果游戏打开了相关界面"), "generic message lost native acceptance");
        });
        await Check("known water items keep their specific messages while native acceptance is the success signal", () =>
        {
            var backend = new FakeItemUse
            {
                UseResult = UseResult(
                    Source("water_tablet", ItemUseRules.WaterTablet, 1),
                    Source("water_tablet", ItemUseRules.WaterTablet, 0),
                    Target("bottle", 100000), Target("bottle", 115000),
                    consumed: true, changed: true)
            };
            var result = new ItemUseService(backend).Execute("use_item_on_item",
                new JObject { ["item_id"] = "water_tablet", ["target_item_id"] = "bottle" });
            Assert(result.Success && result.Message.Contains("净水药片") && result.Message.Contains("115/1000 ml"),
                "tablet message lost");
        });
        await Check("an accepted action with no visible change is still reported as executed, not as a failed use", () =>
        {
            var backend = new FakeItemUse
            {
                UseResult = UseResult(
                    Source("aquascan", "aquascan", 1),
                    Source("aquascan", "aquascan", 1),
                    Target("bottle", 500000), Target("bottle", 500000),
                    consumed: false, changed: false)
            };
            var result = new ItemUseService(backend).Execute("use_item_on_item",
                new JObject { ["item_id"] = "aquascan", ["target_item_id"] = "bottle" });
            Assert(result.Success && result.Code == "ok" && (bool)Doc(result)["data"]["changed"] == false &&
                result.Message.Contains("原生操作已执行"), "accepted no-op was treated as a failed use");
        });
        await Check("activation supports native self-use and reports consumption", () =>
        {
            var backend = new FakeItemUse
            {
                ActivateResult = new ItemUseChange
                {
                    SourceBefore = Source("ration", "water_ration", 1), SourceAfter = null,
                    SourceConsumed = true, Changed = true
                }
            };
            var result = new ItemUseService(backend).Execute("activate_item", new JObject { ["item_id"] = "ration" });
            Assert(result.Success && result.Code == "ok" && (bool)Doc(result)["data"]["source"]["consumed"] &&
                Doc(result)["data"]["target"] == null && result.Message.Contains("已使用"), "activation result lost");
            Assert(backend.Activations == 1, "activation not delegated");
        });
        await Check("item-use rules exclude combat and locked actions but do not whitelist item types", () =>
        {
            var item = Source("teapot", null, 1);
            var other = Target("cup", 0);
            Assert(ItemUseRules.ValidateUse(item, other) == null, "generic item rejected by rules");
            Assert(ItemUseRules.ValidateUse(Source("gun", null, 1, combat: true), other).Code == "combat_item", "combat source accepted");
            Assert(ItemUseRules.ValidateUse(item, Target("enemy", 0, enemy: true)).Code == "combat_target", "enemy target accepted");
            Assert(ItemUseRules.ValidateUse(item, Target("gun", 0, combat: true)) == null, "weapon target rejected");
            Assert(ItemUseRules.ValidateUse(item, item).Code == "same_item", "self use accepted");
            Assert(ItemUseRules.ValidateUse(Source("locked", null, 1, locked: true), other).Code == "container_locked", "locked source accepted");
            Assert(ItemUseRules.ValidateActivate(Source("gun", null, 1, combat: true)).Code == "combat_item", "combat activation accepted");
            Assert(ItemUseRules.ValidateActivate(Source("book", null, 1)) == null, "generic activation rejected");
        });
        await Check("native item-use refusals propagate without claiming success", () =>
        {
            var backend = new FakeItemUse(); var service = new ItemUseService(backend);
            foreach (string code in new[] { "item_not_found", "item_use_rejected", "in_combat", "combat_item" })
            {
                backend.Failure = new HandsException(code, "测试原因");
                Assert(service.Execute("use_item_on_item", new JObject { ["item_id"] = "a", ["target_item_id"] = "b" }).Code == code,
                    "item-use refusal lost: " + code);
                Assert(service.Execute("activate_item", new JObject { ["item_id"] = "a" }).Code == code,
                    "activation refusal lost: " + code);
            }
        });
        await Check("agent uses generic items and activation through the native loop", async () =>
        {
            var backend = new FakeItemUse
            {
                UseResult = UseResult(
                    Source("water_test_strip", "water_test_strip", 1),
                    Source("water_test_strip", "water_test_strip", 1, "用过的试纸"),
                    Target("bottle", 500000), Target("bottle", 500000), consumed: false, changed: true),
                ActivateResult = new ItemUseChange
                {
                    SourceBefore = Source("book", null, 1), SourceAfter = null, SourceConsumed = true, Changed = true
                }
            };
            var registry = new ToolRegistry();
            new ItemUseService(backend).Register(registry);
            int round = 0;
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                if (round++ == 0)
                    return Call("use-1", "use_item_on_item", new JObject { ["item_id"] = "water_test_strip", ["target_item_id"] = "bottle" });
                if (round == 2)
                    return Call("activate-1", "activate_item", new JObject { ["item_id"] = "book" });
                Assert((string)messages.Last["role"] == "tool", "tool result missing");
                return new ChatTurn { Text = "物品已经用好了。" };
            }), registry);
            var result = await agent.RunAsync(new ConnectionOptions(), "用水质试纸测一下这瓶水，再把书用了", null, default);
            Assert(result.ToolCalls == 2 && result.Operations.Count == 2 && backend.Uses == 1 && backend.Activations == 1,
                "generic item actions needed extra calls");
        });
        await Check("item-use messages and schemas follow the game language", () =>
        {
            Text.UseLocale("en");
            try
            {
                var backend = new FakeItemUse
                {
                    UseResult = UseResult(
                        Source("water_test_strip", null, 1), Source("water_test_strip", null, 1, "used strip"),
                        Target("bottle", 500000), Target("bottle", 500000), consumed: false, changed: true)
                };
                var result = new ItemUseService(backend).Execute("use_item_on_item",
                    new JObject { ["item_id"] = "water_test_strip", ["target_item_id"] = "bottle" });
                Assert(result.Success && result.Message.StartsWith("bottle received water_test_strip"), "English item-use message missing");
                var registry = new ToolRegistry();
                new ItemUseService(backend).Register(registry);
                Assert(!registry.Definitions().ToString().Contains("物品使用"), "English item-use schema left untranslated");
            }
            finally { Text.UseLocale("zh"); }
        });
    }

    private static ItemUseReading Source(string id, string kind, int quantity = 1, string name = null,
        bool locked = false, bool combat = false)
        => new ItemUseReading
        {
            Item = new ItemInfo { Id = id, Name = name ?? id, Path = "物品栏 → " + (name ?? id), Quantity = quantity, Owned = true },
            Kind = kind, Quantity = quantity, Locked = locked, Combat = combat
        };

    private static ItemUseReading Target(string id, int volumeUl, int capacityUl = 1000000,
        bool locked = false, bool combat = false, bool enemy = false)
        => new ItemUseReading
        {
            Item = new ItemInfo { Id = id, Name = id, Path = "物品栏 → " + id, Quantity = 1, Owned = true },
            Locked = locked, Combat = combat, Enemy = enemy, LiquidContainer = true, SupportedLiquid = true,
            VolumeUl = volumeUl, CapacityUl = capacityUl
        };

    private static ItemUseChange UseResult(ItemUseReading sourceBefore, ItemUseReading sourceAfter,
        ItemUseReading targetBefore, ItemUseReading targetAfter, bool consumed, bool changed)
        => new ItemUseChange
        {
            SourceBefore = sourceBefore, SourceAfter = sourceAfter,
            TargetBefore = targetBefore, TargetAfter = targetAfter,
            SourceConsumed = consumed, Changed = changed
        };

    private sealed class FakeItemUse : IItemUseBackend
    {
        public ItemUseChange UseResult;
        public ItemUseChange ActivateResult;
        public HandsException Failure;
        public int Uses, Activations;

        public ItemUseChange Use(string sourceItemId, string targetItemId)
        {
            Uses++;
            if (Failure != null) throw Failure;
            return UseResult;
        }

        public ItemUseChange Activate(string itemId)
        {
            Activations++;
            if (Failure != null) throw Failure;
            return ActivateResult;
        }
    }
}
