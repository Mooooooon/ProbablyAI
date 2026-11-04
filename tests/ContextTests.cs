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
        await Check("tool limits default on old configs, normalize, copy and persist", () =>
        {
            var old = new ConnectionSettingsStore(() => "{}", _ => { }).Current;
            Assert(old.MaxToolRounds == 16 && old.MaxToolCalls == 48, "legacy defaults changed");
            foreach (int invalid in new[] { -1, 0, int.MaxValue })
            {
                var normalized = new ConnectionOptions { MaxToolRounds = invalid, MaxToolCalls = invalid }.Normalized();
                Assert(normalized.MaxToolRounds == 16 && normalized.MaxToolCalls == 48, "invalid limits kept");
            }
            foreach (var values in new[] { (1, 1), (1000, 10000), (80, 300) })
            {
                string persisted = null;
                var store = new ConnectionSettingsStore(() => null, value => persisted = value);
                var options = new ConnectionOptions { MaxToolRounds = values.Item1, MaxToolCalls = values.Item2 };
                store.Save(options.Copy());
                var restored = new ConnectionSettingsStore(() => persisted, _ => { }).Current;
                Assert(restored.MaxToolRounds == values.Item1 && restored.MaxToolCalls == values.Item2, "limits not persisted");
            }
            var memory = new ConversationMemory();
            memory.BindConnection(old);
            memory.Commit(Exchange("hello", "hi"));
            old.MaxToolRounds = 80; old.MaxToolCalls = 300;
            Assert(!memory.BindConnection(old) && memory.TurnCount == 1, "tool limits cleared history");
        });
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
        await Check("memory keeps the complete archive while sending only whole turns within limits", () =>
        {
            var memory = new ConversationMemory(10, 100000);
            for (int i = 0; i < 5; i++)
                memory.Commit(Exchange("u" + i, "a" + i));
            Assert(memory.TurnCount == 5 && memory.Messages().Count == 10, "archive did not retain every turn");
            var context = memory.ContextMessages(2, 100000, _ => false);
            Assert(context.Count == 4 && (string)context.First["content"] == "u3" && memory.TurnCount == 5, "turn limit changed the archive or chose the wrong turns");
            memory.Clear();
            for (int i = 0; i < 3; i++)
                memory.Commit(ToolExchange("read-" + i, "find_items", new string('a', 3000)));
            int readBudget = TokenEstimator.EstimateMany(memory.ContextMessages(10, 1000000, _ => false));
            Assert(memory.ContextMessages(10, readBudget, _ => false).Count == 6, "read-only projection did not fit its own budget");
            memory.Clear();
            for (int i = 0; i < 3; i++)
                memory.Commit(ToolExchange("move-" + i, "move_item", new string('a', 3000)));
            Assert(memory.ContextMessages(10, readBudget, name => name == "move_item").Count < 6, "token budget kept oversized retained results");
            Expect<ArgumentOutOfRangeException>(() => memory.ContextMessages(0, 1000, _ => false));
            Expect<ArgumentOutOfRangeException>(() => memory.ContextMessages(1, 0, _ => false));
        });
        await Check("agent applies configured send limits without deleting archived turns", async () =>
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
            Assert(memory.TurnCount == 6 && memory.Messages().Count == 12, "request context deleted archived turns");
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
        await Check("conversation history is restored from storage and survives a new memory instance", () =>
        {
            var store = new FakeConversationStore();
            var options = new ConnectionOptions();
            var memory = new ConversationMemory(store: store);
            Assert(!memory.BindConnection(options), "initial bind cleared context");
            memory.Commit(Exchange("主人，在吗？", "在的，主人。"));
            memory.Commit(ToolExchange("move-1", "move_item", "{\"moved\":1}"));
            Assert(store.Saves >= 2 && !string.IsNullOrEmpty(store.Text), "commits were not persisted");
            var restored = new ConversationMemory(store: store);
            Assert(!restored.BindConnection(options), "restored context rejected its own connection");
            Assert(restored.TurnCount == 2 && restored.Messages().Count == 4, "saved turns were not restored");
            Assert(restored.ContextMessages(8, 32000, name => name == "move_item").Count == 4, "restored tool pair was split");
            var readProjection = restored.ContextMessages(8, 32000, _ => false);
            Assert(readProjection.Count == 4 &&
                (string)JObject.Parse((string)readProjection[3]["content"])["code"] == "historical_read_omitted",
                "restored read projection did not omit its stale result");
        });
        await Check("loading a checkpoint restores only its own chat and rejects late commits from the old run", () =>
        {
            // The game snapshots its current mod data on save and replaces it on load.
            string activeData = null;
            var store = new ConversationStore(() => activeData, text => activeData = text, () => activeData = null);
            var current = new ConversationMemory(store: store);
            current.Commit(Exchange("before save", "saved"));
            string checkpoint = activeData;
            current.Commit(Exchange("after save", "rolled back"));
            Assert(activeData != checkpoint, "later chat did not reach live mod data");

            current.DetachStore();
            activeData = checkpoint;
            var restored = new ConversationMemory(store: store);
            current.Commit(Exchange("old request finishing", "discarded"));
            Assert(activeData == checkpoint, "old request overwrote the loaded save");
            var restoredContext = restored.ContextMessages(8, 32000, _ => false);
            Assert(restoredContext.Count == 2 && (string)restoredContext[0]["content"] == "before save",
                "model context did not roll back with the game");

            restored.Clear();
            Assert(activeData == null, "clear did not update the next save");
            Assert(new ConversationMemory(store: store).TurnCount == 0, "empty save inherited chat from another run");
            activeData = checkpoint;
            Assert(new ConversationMemory(store: store).TurnCount == 1, "clearing live chat changed an earlier checkpoint");
        });
        await Check("stored history belongs to one connection and can be cleared manually", () =>
        {
            var store = new FakeConversationStore();
            var first = new ConnectionOptions { Model = "first-model" };
            var memory = new ConversationMemory(store: store);
            memory.BindConnection(first);
            memory.Commit(Exchange("旧对话", "旧回复"));
            var changed = new ConnectionOptions { Model = "second-model" };
            var next = new ConversationMemory(store: store);
            Assert(next.BindConnection(changed) && next.TurnCount == 0 && store.Text == null, "stored context moved across connections");
            next.Commit(Exchange("新对话", "新回复"));
            Assert(store.Text != null && next.TurnCount == 1, "new connection was not persisted");
            next.Clear();
            Assert(next.TurnCount == 0 && store.Text == null && store.Deletes > 0, "manual clear kept the saved file");
            Assert(new ConversationMemory(store: store).TurnCount == 0, "cleared history came back");
        });
        await Check("local notices and corrupt files do not pollute restored history", () =>
        {
            var store = new FakeConversationStore();
            var memory = new ConversationMemory(store: store);
            memory.PostNotice("（小声）主人，这个人符合通缉令上的特征呢。");
            memory.Commit(Exchange("你好", "主人好。"));
            var restored = new ConversationMemory(store: store);
            Assert(restored.Notices().Count == 0 && restored.TurnCount == 1, "local notice was restored");
            store.Text = "{broken-json";
            var corrupt = new ConversationMemory(store: store);
            Assert(corrupt.TurnCount == 0 && !string.IsNullOrEmpty(corrupt.LoadWarning), "corrupt history was accepted");
            store.Text = new string('x', 17 * 1024 * 1024);
            var oversized = new ConversationMemory(store: store);
            Assert(oversized.TurnCount == 0 && !string.IsNullOrEmpty(oversized.LoadWarning), "oversized history was accepted");
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

    private sealed class FakeConversationStore : IConversationStore
    {
        public string Text;
        public int Saves, Deletes;
        public string Load() => Text;
        public void Save(string text) { Text = text; Saves++; }
        public void Delete() { Text = null; Deletes++; }
    }
}
