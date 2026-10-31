using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Hands;
using ProbablyAI.Localization;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunFreshnessTests()
    {
        await Check("model history omits old reads but preserves past actions, pairs and visible chat", () =>
        {
            var memory = new ConversationMemory();
            var read = Call("old-read", "list_container", new JObject { ["container_id"] = "counter" });
            var move = Call("old-move", "move_item", Move("ore", "inventory", "counter", 1));
            memory.Commit(new JArray(new JObject { ["role"] = "user", ["content"] = "之前那组物品" }, read.Message,
                RecordedTool("old-read", new JObject { ["success"] = true, ["data"] = new JObject { ["old-price"] = 97 } }),
                move.Message, RecordedTool("old-move", new JObject { ["success"] = true, ["data"] = new JObject { ["moved"] = 1 } }),
                new JObject { ["role"] = "assistant", ["content"] = "已经放到柜台。" }));
            string original = memory.Messages().ToString();
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            var model = memory.MessagesForModel(registry.ChangesState);
            var replies = model.OfType<JObject>().Where(m => (string)m["role"] == "tool").Select(m => JObject.Parse((string)m["content"])).ToArray();
            Assert((string)replies[0]["code"] == "historical_read_omitted" && replies[0]["data"] == null, "old query resent as state");
            Assert((bool)replies[1]["historical"] && (int)replies[1]["data"]["moved"] == 1, "past operation lost");
            Assert(memory.Messages().ToString() == original && (string)model.Last["content"] == "已经放到柜台。", "visible history changed");
            Assert(model.OfType<JObject>().Where(m => m["tool_calls"] != null).Count() == 2 &&
                model.OfType<JObject>().Count(m => (string)m["role"] == "tool") == 2, "tool pairs split");
        });
        await Check("next message refreshes a changed customer and an empty counter after a sale", async () =>
        {
            var hands = new FakeHands(); var customer = new FakeCustomer(); var memory = new ConversationMemory();
            foreach (var entry in new[] { ("toothpaste", "牙膏", 2), ("paper", "厕纸", 1), ("detergent", "洗涤剂", 1) })
                hands.World.Items.Add(new ItemInfo { Id = entry.Item1, Name = entry.Item2, ContainerId = "counter", Quantity = entry.Item3, Owned = true });
            var registry = ToolRegistry.ForHands(new HandsService(hands)); new CustomerService(customer).Register(registry);
            int round = 0;
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                var liveCustomer = FreshResult(messages, "get_current_customer"); var counter = FreshResult(messages, "list_container");
                if (round++ == 0)
                {
                    Assert((string)liveCustomer["data"]["customer"]["id"] == "guest-one" && (int)counter["data"]["items"]["total"] == 3, "initial state missing");
                    return new ChatTurn { Text = "这组日用品共97，已摆到柜台。" };
                }
                Assert((string)liveCustomer["data"]["customer"]["id"] == "guest-two" &&
                    (int)liveCustomer["data"]["customer"]["purchase_budget"]["remaining"] == 50, "previous customer reused");
                Assert((int)counter["data"]["items"]["total"] == 0 && !((JArray)counter["data"]["items"]["entries"]).Any(), "sold goods still in new observation");
                Assert(messages.OfType<JObject>().All(m => (string)m["role"] != "tool"), "automatic snapshots were saved into history");
                Assert(messages.Any(m => (string)m["content"] == "这组日用品共97，已摆到柜台。"), "conversation context unnecessarily erased");
                return new ChatTurn { Text = "主人，当前柜台是空的，这位客人只买食物。" };
            }), registry, refreshShopContext: true);
            await agent.RunAsync(new ConnectionOptions(), "帮我看看这组日用品", null, default, memory);
            hands.World.Items.RemoveAll(i => i.ContainerId == "counter");
            customer.Current.Customer.Id = "guest-two"; customer.Current.Customer.PurchaseBudget.Remaining = 50;
            customer.Current.Customer.Preferences.BuyingTags.Clear(); customer.Current.Customer.Preferences.BuyingTags.Add("FOOD");
            var response = await agent.RunAsync(new ConnectionOptions(), "下一位客人想要什么？", null, default, memory);
            Assert(response.ToolCalls == 2 && response.Operations.Count == 0 && hands.Moves == 0 && customer.Reads == 2, "automatic refresh mutated state or reused cache");
        });
        await Check("sold objects remaining at the counter retain current ownership in the fresh snapshot", async () =>
        {
            var hands = new FakeHands(); hands.World.Items.Add(new ItemInfo { Id = "sold", Name = "已售牙膏", ContainerId = "counter", Owned = false, Quantity = 2 });
            var registry = ToolRegistry.ForHands(new HandsService(hands)); new CustomerService(new FakeCustomer()).Register(registry);
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                var item = FreshResult(messages, "list_container")["data"]["items"]["entries"][0];
                Assert(!(bool)item["owned"] && (int)item["quantity"] == 2, "sold item presented as owned merchandise");
                return new ChatTurn { Text = "已经售出了。" };
            }), registry, true);
            await agent.RunAsync(new ConnectionOptions(), "那组货还没卖吗？", null, default);
        });
        await Check("failed state refresh sends explicit failure without falling back to historical state", async () =>
        {
            var hands = new FakeHands(); hands.World.Containers.RemoveAll(c => c.Id == "counter");
            var registry = ToolRegistry.ForHands(new HandsService(hands));
            new CustomerService(new FakeCustomer { Failure = new HandsException("store_unavailable", "无法读取。") }).Register(registry);
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                Assert(!(bool)FreshResult(messages, "get_current_customer")["success"] && !(bool)FreshResult(messages, "list_container")["success"], "failed refresh became a successful empty snapshot");
                return new ChatTurn { Text = "现在无法读取，请稍后再试。" };
            }), registry, true);
            await agent.RunAsync(new ConnectionOptions(), "现在柜台有什么？", null, default);
            Assert(hands.Moves == 0, "failure triggered an operation");
        });
        await Check("automatic context cannot execute mutations or unrelated future tools", async () =>
        {
            var registry = new ToolRegistry(); int mutations = 0, futureReads = 0;
            registry.Register(FutureTool("list_container"), _ => { mutations++; return HandsResult.Ok(null); }, true);
            registry.Register(FutureTool("future_reader"), _ => { futureReads++; return HandsResult.Ok(null); });
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                Assert(messages.Count == 2, "unsupported or mutating context tool was added to the wire");
                return new ChatTurn { Text = "你好。" };
            }), registry, true);
            await agent.RunAsync(new ConnectionOptions(), "你好", null, default);
            Assert(mutations == 0 && futureReads == 0, "automatic refresh executed a non-allowlisted tool");
        });
        await Check("cancelled message performs no automatic reads or HTTP request", async () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands())); var customer = new FakeCustomer(); new CustomerService(customer).Register(registry);
            var agent = new HandsAgent(new FakeTransport((_, _) => throw new Exception("HTTP should not run")), registry, true);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ExpectAsync<OperationCanceledException>(() => agent.RunAsync(new ConnectionOptions(), "现在呢", null, cancellation.Token));
            Assert(customer.Reads == 0 && !agent.IsRunning, "cancelled refresh executed");
        });
        await Check("fresh snapshot preserves pagination and does not imply an incomplete counter is empty", async () =>
        {
            var hands = new FakeHands();
            for (int i = 0; i < 101; i++) hands.World.Items.Add(new ItemInfo { Id = "counter-" + i, ContainerId = "counter" });
            var registry = ToolRegistry.ForHands(new HandsService(hands));
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                var items = FreshResult(messages, "list_container")["data"]["items"];
                Assert((int)items["total"] == 101 && (bool)items["has_more"] && ((JArray)items["entries"]).Count == 100, "snapshot completeness hidden");
                return new ChatTurn { Text = "需要继续查询下一页。" };
            }), registry, true);
            await agent.RunAsync(new ConnectionOptions(), "柜台有什么？", null, default);
        });
        await Check("automatic observations preserve custom prompt and user text without synthetic model messages", async () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands())); new CustomerService(new FakeCustomer()).Register(registry);
            var agent = new HandsAgent(new FakeTransport((messages, token) =>
            {
                Assert(messages.Count(m => (string)m["role"] == "system") == 1 && (string)messages[0]["content"] == "主人的自定义设定", "custom prompt replaced or appended");
                Assert((string)messages.Last["content"] == "保持我的原话", "user text changed");
                Assert(messages.OfType<JObject>().All(m => m["tool_calls"] == null && m["reasoning_content"] == null), "automatic context fabricated model tool calls or reasoning");
                Assert(FreshResult(messages, "get_current_customer")["data"] != null, "context data missing");
                return new ChatTurn { Text = "好哒。" };
            }), registry, true);
            await agent.RunAsync(new ConnectionOptions { SystemPrompt = "主人的自定义设定" }, "保持我的原话", null, default);
        });
        await Check("old unedited default prompts upgrade naturally and edited defaults stay untouched", () =>
        {
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseDefault, AssistantPrompt.PreviousEnglishDefault,
                AssistantPrompt.PreviousChineseFreshnessDefault, AssistantPrompt.PreviousEnglishFreshnessDefault,
                AssistantPrompt.PreviousChineseInspectionDefault, AssistantPrompt.PreviousEnglishInspectionDefault,
                AssistantPrompt.PreviousChineseTaskDefault, AssistantPrompt.PreviousEnglishTaskDefault,
                AssistantPrompt.PreviousChineseWantedDefault, AssistantPrompt.PreviousEnglishWantedDefault,
                AssistantPrompt.PreviousChineseDocumentDefault, AssistantPrompt.PreviousEnglishDocumentDefault,
                AssistantPrompt.PreviousChineseAugDefault, AssistantPrompt.PreviousEnglishAugDefault,
                AssistantPrompt.PreviousChineseBatteryDefault, AssistantPrompt.PreviousEnglishBatteryDefault,
                AssistantPrompt.PreviousChineseChargingDefault, AssistantPrompt.PreviousEnglishChargingDefault,
                AssistantPrompt.PreviousChinesePinnedDefault, AssistantPrompt.PreviousEnglishPinnedDefault,
                AssistantPrompt.PreviousChineseRecorderDefault, AssistantPrompt.PreviousEnglishRecorderDefault,
                AssistantPrompt.PreviousChineseStolenDefault, AssistantPrompt.PreviousEnglishStolenDefault,
                AssistantPrompt.PreviousChineseRibwichDefault, AssistantPrompt.PreviousEnglishRibwichDefault,
                AssistantPrompt.PreviousChinesePlasmaDefault, AssistantPrompt.PreviousEnglishPlasmaDefault,
                AssistantPrompt.PreviousChineseBatchDefault, AssistantPrompt.PreviousEnglishBatchDefault,
                AssistantPrompt.PreviousChineseVarietyDefault, AssistantPrompt.PreviousEnglishVarietyDefault })
            {
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "old built-in prompt did not upgrade");
                Assert(AssistantPrompt.Normalize(previous + "\nCustom addition") == previous + "\nCustom addition", "customized built-in prompt replaced");
            }
            Assert(AssistantPrompt.Default.Contains("历史对话只记录过去") && AssistantPrompt.Default.Contains("自然简洁"), "freshness or concise response guidance missing");
        });
        await Check("freshness context follows game language without changing user dialogue", async () =>
        {
            Text.UseLocale("en");
            try
            {
                var registry = ToolRegistry.ForHands(new HandsService(new FakeHands())); new CustomerService(new FakeCustomer()).Register(registry);
                var agent = new HandsAgent(new FakeTransport((messages, token) =>
                {
                    Assert(((string)FreshResult(messages, "list_container")["context_note"]).StartsWith("Freshly read"), "context note untranslated");
                    return new ChatTurn { Text = "Of course, Master." };
                }), registry, true);
                await agent.RunAsync(new ConnectionOptions(), "我的原话", null, default);
                Assert(AssistantPrompt.Normalize(AssistantPrompt.PreviousChineseDefault) == AssistantPrompt.EnglishDefault, "historical default ignored current locale");
            }
            finally { Text.UseLocale("zh"); }
        });
    }

    private static JObject RecordedTool(string id, JObject result) => new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result.ToString() };
    private static JObject FreshResult(JArray messages, string name)
    {
        var observation = messages.OfType<JObject>().Where(m => (string)m["role"] == "user" && ((string)m["content"])?.StartsWith("{\"kind\":\"shop_observation\"") == true)
            .Select(m => JObject.Parse((string)m["content"])).Single();
        Assert((string)observation["observation_scope"] == "request_start", "fresh observation metadata missing");
        var result = (JObject)observation["observations"].Single(o => (string)o["tool"] == name)["result"];
        var copy = (JObject)result.DeepClone(); copy["context_note"] = observation["context_note"];
        return copy;
    }
}
