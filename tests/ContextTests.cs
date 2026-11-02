using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Services;

internal static partial class Program
{
    private static async Task RunContextTests()
    {
        await Check("history limits have safe defaults, normalize invalid values and persist", () =>
        {
            var defaults = new ConnectionOptions();
            Assert(defaults.MaxHistoryTurns == 8 && defaults.MaxHistoryTokens == 32000, "history defaults changed");
            var invalid = new ConnectionOptions { MaxHistoryTurns = 0, MaxHistoryTokens = 999 }.Normalized(false);
            Assert(invalid.MaxHistoryTurns == 8 && invalid.MaxHistoryTokens == 32000, "invalid history limits kept");
            var custom = new ConnectionOptions { MaxHistoryTurns = 20, MaxHistoryTokens = 50000 }.Normalized(false);
            var copy = custom.Copy();
            copy.MaxHistoryTurns = 21;
            Assert(custom.MaxHistoryTurns == 20 && copy.MaxHistoryTurns == 21, "history limits not copied");
            string persisted = null;
            var store = new ConnectionSettingsStore(() => null, value => persisted = value);
            store.Save(custom);
            var reloaded = new ConnectionSettingsStore(() => persisted, _ => { });
            Assert(reloaded.Current.MaxHistoryTurns == 20 && reloaded.Current.MaxHistoryTokens == 50000, "history limits not persisted");
        });
        await Check("token estimates grow with text and stay provider independent", () =>
        {
            Assert(TokenEstimator.EstimateText(null) == 0 && TokenEstimator.EstimateText("") == 0, "empty text estimated");
            Assert(TokenEstimator.EstimateText("中") == 1 && TokenEstimator.EstimateText("中文") == 2, "Chinese estimate changed");
            Assert(TokenEstimator.EstimateText("a") == 1 && TokenEstimator.EstimateText("abcd") == 2, "ASCII estimate changed");
            Assert(TokenEstimator.EstimateText(new string('中', 100)) > TokenEstimator.EstimateText(new string('中', 10)), "estimate not monotonic");
            var message = new JObject { ["role"] = "user", ["content"] = "你好" };
            Assert(TokenEstimator.Estimate(message) >= 5, "message overhead missing");
        });
        await Check("memory trims whole turns by count and respects a token budget", () =>
        {
            var memory = new ConversationMemory(10, 100000);
            for (int i = 0; i < 5; i++)
                memory.Commit(Exchange("u" + i, "a" + i));
            Assert(memory.TurnCount == 5, "turns lost before trimming");
            memory.TrimToContext(2, 100000, _ => false);
            Assert(memory.TurnCount == 2 && (string)memory.Messages().First["content"] == "u3", "turn limit did not trim oldest turns");
            memory.Clear();
            memory.Configure(10, 100000);
            for (int i = 0; i < 3; i++)
                memory.Commit(ToolExchange("read-" + i, "find_items", new string('a', 3000)));
            int readBudget = memory.EstimateForModel(_ => false);
            memory.TrimToContext(10, readBudget, _ => false);
            Assert(memory.TurnCount == 3, "read-only projection exceeded its own budget: kept " + memory.TurnCount +
                ", budget " + readBudget + ", estimate " + memory.EstimateForModel(_ => false));
            memory.Clear();
            memory.Configure(10, 100000);
            for (int i = 0; i < 3; i++)
                memory.Commit(ToolExchange("move-" + i, "move_item", new string('a', 3000)));
            int moveBudget = memory.EstimateForModel(name => name == "move_item");
            Assert(moveBudget > readBudget, "past operations counted like omitted reads");
            memory.TrimToContext(10, readBudget, name => name == "move_item");
            Assert(memory.TurnCount < 3, "token budget kept oversized retained results");
            Expect<ArgumentOutOfRangeException>(() => memory.Configure(0, 1000));
            Expect<ArgumentOutOfRangeException>(() => memory.Configure(1, 0));
        });
        await Check("agent applies configured turn and token limits before each request", async () =>
        {
            var memory = new ConversationMemory(10, 100000);
            for (int i = 0; i < 5; i++)
                memory.Commit(Exchange("u" + i, "a" + i));
            var options = new ConnectionOptions { MaxHistoryTurns = 2, MaxHistoryTokens = 32000 };
            var transport = new FakeTransport((messages, token) =>
            {
                Assert(messages.Count == 6, "history limits not applied before the request");
                Assert((string)messages[1]["content"] == "u3" && (string)messages[2]["content"] == "a3", "wrong turns survived");
                Assert((string)messages.Last["content"] == "现在", "current message missing");
                return new ChatTurn { Text = "好哒。" };
            });
            await new HandsAgent(transport, new HandsService(new FakeHands()))
                .RunAsync(options, "现在", null, default, memory);
            Assert(memory.TurnCount == 2, "current exchange not committed after trimming: " + memory.TurnCount);
        });
        await Check("changing only the history limits keeps the conversation", () =>
        {
            var memory = new ConversationMemory();
            var options = new ConnectionOptions();
            Assert(!memory.BindConnection(options), "initial bind cleared context");
            memory.Commit(Exchange("你好", "主人你好。"));
            var limited = options.Copy();
            limited.MaxHistoryTurns = 20;
            limited.MaxHistoryTokens = 50000;
            Assert(!memory.BindConnection(limited) && memory.TurnCount == 1, "history limits treated as a new connection");
            limited.Model = "another-model";
            Assert(memory.BindConnection(limited) && memory.TurnCount == 0, "model change kept context");
        });
    }

    private static JArray Exchange(string user, string assistant)
        => new JArray(
            new JObject { ["role"] = "user", ["content"] = user },
            new JObject { ["role"] = "assistant", ["content"] = assistant });

    private static JArray ToolExchange(string id, string name, string payload)
    {
        var content = new JObject { ["data"] = payload }.ToString();
        return new JArray(
            new JObject
            {
                ["role"] = "assistant", ["content"] = JValue.CreateNull(),
                ["tool_calls"] = new JArray(new JObject
                {
                    ["id"] = id, ["type"] = "function",
                    ["function"] = new JObject { ["name"] = name, ["arguments"] = "{\"query\":\"x\"}" }
                })
            },
            new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = content });
    }
}
