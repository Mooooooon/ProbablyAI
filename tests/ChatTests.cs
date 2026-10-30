using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI;
using ProbablyAI.Hands;
using ProbablyAI.Services;
using ProbablyAI.Tools;
using ProbablyAI.UI;

internal static partial class Program
{
    private static async Task RunChatTests()
    {
        await Check("branding preserves the requested exact display name", () =>
            Assert(Branding.Name == "深空AI：可能是DeepSeek" && Branding.Version == "0.6.1", "branding mismatch"));
        await Check("portrait alpha hit test passes through transparent pixels and bounds", () =>
        {
            var mask = new AlphaHitMask(2, 2, new byte[] { 0, 254, 12, 240 });
            Assert(!mask.Hit(.25f, .25f) && mask.Hit(.75f, .25f), "bottom alpha hit test");
            Assert(!mask.Hit(.25f, .75f) && mask.Hit(.75f, .75f), "top alpha hit test");
            Assert(!mask.Hit(-.1f, 0f) && !mask.Hit(1f, .5f) && !mask.Hit(.5f, 1f) && !mask.Hit(float.NaN, .5f), "out-of-bounds hit");
            Expect<ArgumentException>(() => new AlphaHitMask(2, 2, new byte[1]));
        });
        await Check("food query uses the game category, including bread and cans", () =>
        {
            var backend = FoodWorld(); var hands = new HandsService(backend);
            foreach (var query in new[]
            {
                new JObject { ["item_type"] = "FOOD", ["owned_only"] = true, ["container_id"] = "inventory" },
                new JObject { ["query"] = "食物", ["owned_only"] = true, ["container_id"] = "inventory" },
                new JObject { ["query"] = "食品", ["item_type"] = "FOOD", ["owned_only"] = true, ["container_id"] = "inventory" }
            })
            {
                var entries = (JArray)Doc(hands.Execute("find_items", query))["data"]["page"]["entries"];
                Assert(entries.Count == 2 && entries.Any(i => (string)i["name"] == "面包") && entries.Any(i => (string)i["name"] == "罐头"), "food category failed");
                Assert(entries.All(i => (bool)i["owned"]), "customer food included");
            }
            Assert(!hands.Execute("find_items", new JObject { ["item_type"] = "FOOD", ["owned_only"] = "true" }).Success, "bad boolean accepted");
        });
        await Check("natural food command queries then moves each food stack to the counter", async () =>
        {
            var backend = FoodWorld(); int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (round++ == 0) return Call("food-find", "find_items", new JObject { ["item_type"] = "FOOD", ["container_id"] = "inventory", ["owned_only"] = true });
                if (round == 2)
                {
                    var entries = (JArray)JObject.Parse((string)messages.Last["content"])["data"]["page"]["entries"];
                    Assert(entries.Count == 2, "wrong food selection");
                    return Batch(
                        Call("food-one", "move_item", Move((string)entries[0]["id"], (string)entries[0]["container_id"], "counter", 0)),
                        Call("food-two", "move_item", Move((string)entries[1]["id"], (string)entries[1]["container_id"], "counter", 0)));
                }
                return new ChatTurn { Text = "已把面包和罐头放到柜台。" };
            });
            await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "帮我把食物放到柜台上", null, default);
            Assert(backend.World.Items.Where(i => i.Id == "bread" || i.Id == "can").All(i => i.ContainerId == "counter"), "food did not move");
            Assert(backend.World.Items.First(i => i.Id == "ore").ContainerId == "inventory" && backend.World.Items.First(i => i.Id == "customer-food").ContainerId == "customer", "unrelated item moved");
        });
        await Check("tool registry clones definitions and rejects duplicate or missing tools", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            var definition = FutureTool("future_tool"); registry.Register(definition, _ => HandsResult.Ok("result"), true);
            definition["function"]["name"] = "changed";
            Assert(registry.Definitions().Any(t => (string)t["function"]["name"] == "future_tool"), "definition mutated from outside");
            Assert(registry.ChangesState("future_tool") && !registry.ChangesState(null), "effect metadata");
            Expect<ArgumentException>(() => registry.Register(FutureTool("future_tool"), _ => HandsResult.Ok("duplicate")));
            Assert(registry.Execute("unavailable", new JObject()).Code == "unknown_tool", "unknown tool dispatched");
        });
        await Check("tools registered after agent construction are offered and callable", async () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands())); bool executed = false; int requests = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(tools.Any(t => (string)t["function"]["name"] == "future_sorter"), "future tool was not offered");
                return requests++ == 0 ? Call("future-call", "future_sorter", new JObject()) : new ChatTurn { Text = "新工具已完成。" };
            });
            var agent = new HandsAgent(transport, registry);
            registry.Register(FutureTool("future_sorter"), _ => { executed = true; return HandsResult.Ok("sorted", "整理完成。"); }, true);
            var result = await agent.RunAsync(new ConnectionOptions(), "使用新工具", null, default);
            Assert(executed && result.Operations.Single() == "整理完成。", "future tool not executed/audited");
        });
        await Check("chat remembers a prior exchange for natural follow-up messages", async () =>
        {
            var memory = new ConversationMemory(); int requests = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (requests++ == 0) return new ChatTurn { Text = "你好，我是深空AI。" };
                Assert((string)messages[1]["content"] == "你好" && (string)messages[2]["content"] == "你好，我是深空AI。", "prior exchange missing");
                Assert((string)messages.Last["content"] == "接着聊", "current user message missing");
                return new ChatTurn { Text = "当然。" };
            });
            var agent = new HandsAgent(transport, new HandsService(new FakeHands()));
            await agent.RunAsync(new ConnectionOptions(), "你好", null, default, memory);
            await agent.RunAsync(new ConnectionOptions(), "接着聊", null, default, memory);
            Assert(memory.TurnCount == 2, "chat not committed");
        });
        await Check("cancellation records unexecuted calls and keeps tool history valid", async () =>
        {
            var backend = new FakeHands(); var memory = new ConversationMemory();
            using var cancellation = new CancellationTokenSource();
            var transport = new FakeTransport((messages, token) => Batch(
                Call("first", "move_item", Move("note-a", "mentor", "counter", 1)),
                Call("second", "move_item", Move("note-b", "mentor", "trash", 1))));
            var agent = new HandsAgent(transport, new HandsService(backend));
            await ExpectAsync<OperationCanceledException>(() => agent.RunAsync(new ConnectionOptions(), "移动两份笔记", _ =>
            { if (backend.Moves == 1) cancellation.Cancel(); }, cancellation.Token, memory));
            var history = memory.Messages();
            var results = history.OfType<JObject>().Where(m => (string)m["role"] == "tool").ToArray();
            Assert(results.Length == 2 && (string)JObject.Parse((string)results[1]["content"])["code"] == "not_executed", "dangling tool call in history");
            Assert(backend.Moves == 1 && !agent.IsRunning && (string)history.Last["role"] == "assistant", "cancellation cleanup");
        });
        await Check("history trimming retains complete exchanges and clear removes them", () =>
        {
            var memory = new ConversationMemory(2, 10000);
            for (int i = 0; i < 3; i++) memory.Commit(new JArray(new JObject { ["role"] = "user", ["content"] = "u" + i }, new JObject { ["role"] = "assistant", ["content"] = "a" + i }));
            var messages = memory.Messages();
            Assert(messages.Count == 4 && (string)messages.First["content"] == "u1" && (string)messages.Last["content"] == "a2", "exchange split while trimming");
            memory.Clear(); Assert(memory.Messages().Count == 0, "history not cleared");
            Expect<ArgumentOutOfRangeException>(() => new ConversationMemory(0));
        });
        await Check("changing provider, key or model clears chat context without storing a key in it", () =>
        {
            var memory = new ConversationMemory(); var options = new ConnectionOptions { ApiKey = "test-only-chat-secret" };
            Assert(!memory.BindConnection(options), "initial bind should not change context");
            memory.Commit(new JArray(new JObject { ["role"] = "user", ["content"] = "hello" }));
            Assert(!memory.BindConnection(options.Copy()) && memory.TurnCount == 1, "same connection cleared context");
            options.Model = "new-model";
            Assert(memory.BindConnection(options) && memory.TurnCount == 0, "model change kept context");
            memory.Commit(new JArray(new JObject { ["role"] = "user", ["content"] = "hello" })); options.ApiKey = "new-test-secret";
            Assert(memory.BindConnection(options) && memory.TurnCount == 0 && !memory.Messages().ToString().Contains("test-secret"), "key change kept context");
        });
        await Check("simultaneous requests cannot run a second tool loop", async () =>
        {
            var release = new TaskCompletionSource<ChatTurn>();
            var agent = new HandsAgent(new BlockingTransport(release.Task), new HandsService(new FakeHands()));
            var first = agent.RunAsync(new ConnectionOptions(), "第一条", null, default);
            Assert(agent.IsRunning, "running state not set");
            await ExpectAsync<InvalidOperationException>(() => agent.RunAsync(new ConnectionOptions(), "第二条", null, default));
            release.SetResult(new ChatTurn { Text = "完成。" }); await first;
            Assert(!agent.IsRunning, "running state not cleared");
        });
    }

    private static FakeHands FoodWorld()
    {
        var backend = new FakeHands();
        foreach (var data in new[] { ("bread", "面包", "inventory", true), ("can", "罐头", "inventory", true), ("customer-food", "饼干", "customer", false) })
            backend.World.Items.Add(new ItemInfo { Id = data.Item1, Name = data.Item2, Identifier = data.Item1, ContainerId = data.Item3,
                Path = data.Item3 + " → " + data.Item2, Quantity = 2, Owned = data.Item4, Removable = true, Types = new System.Collections.Generic.List<string> { "FOOD" } });
        return backend;
    }
    private static JObject FutureTool(string name) => new JObject
    {
        ["type"] = "function", ["function"] = new JObject { ["name"] = name, ["description"] = "Future test tool",
            ["parameters"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray(), ["additionalProperties"] = false } }
    };
    private static ChatTurn Batch(params ChatTurn[] turns)
    {
        var turn = turns[0];
        var calls = turns.SelectMany(t => t.ToolCalls).ToArray();
        var raw = new JArray(turns.SelectMany(t => (JArray)t.Message["tool_calls"]).Select(t => t.DeepClone()));
        turn.ToolCalls = calls; turn.Message["tool_calls"] = raw; return turn;
    }
    private sealed class RegistryTransport : IChatTransport
    {
        private readonly Func<JArray, JArray, CancellationToken, ChatTurn> reply;
        public RegistryTransport(Func<JArray, JArray, CancellationToken, ChatTurn> reply) => this.reply = reply;
        public Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken token) => Task.FromResult(reply(messages, tools, token));
    }
    private sealed class BlockingTransport : IChatTransport
    {
        private readonly Task<ChatTurn> task;
        public BlockingTransport(Task<ChatTurn> task) => this.task = task;
        public Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken token) => task;
    }
}
