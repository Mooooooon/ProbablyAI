using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunPromptTests()
    {
        await Check("old connection files gain the maid prompt while preserving their connection", () =>
        {
            var store = new ConnectionSettingsStore(() => "{\"BaseUrl\":\"https://example.test/v1\",\"ApiKey\":\"test-only-key\",\"Model\":\"saved-model\"}", _ => { });
            var options = store.Current;
            Assert(options.BaseUrl == "https://example.test/v1" && options.Model == "saved-model" && options.ApiKey == "test-only-key", "migration changed connection");
            Assert(store.LoadWarning == null && options.SystemPrompt == AssistantPrompt.Default, "default prompt not adopted");
            Assert(options.SystemPrompt.Contains("女仆助手") && options.SystemPrompt.Contains("主人") && options.SystemPrompt.Contains("活泼"), "requested default persona missing");
        });
        await Check("custom multiline prompts survive saving, copying and reload; empty resets to default", () =>
        {
            string persisted = null;
            string custom = "你是主人的女仆。\r\n\r\n保留 <b>这些文字</b>、引号“好哒”和段落。\n  缩进也保留。";
            var store = new ConnectionSettingsStore(() => null, value => persisted = value);
            store.Save(new ConnectionOptions { ApiKey = "test-only-not-persisted", RememberApiKey = false, SystemPrompt = custom });
            var reloaded = new ConnectionSettingsStore(() => persisted, _ => { });
            Assert(reloaded.Current.SystemPrompt == custom && !persisted.Contains("test-only-not-persisted"), "prompt lost formatting or key persisted");
            var copy = store.Current; copy.SystemPrompt = "changed draft";
            Assert(store.Current.SystemPrompt == custom, "editing draft changed saved prompt");
            foreach (string empty in new[] { null, "", " \r\n " })
            {
                store.Save(new ConnectionOptions { SystemPrompt = empty });
                Assert(store.Current.SystemPrompt == AssistantPrompt.Default, "blank prompt did not reset");
            }
        });
        await Check("overlong prompts do not replace saved configuration or reach persistence", () =>
        {
            int writes = 0;
            var store = new ConnectionSettingsStore(() => null, _ => writes++);
            var valid = new ConnectionOptions { SystemPrompt = new string('鲸', AssistantPrompt.MaxLength) };
            store.Save(valid);
            var invalid = valid.Copy(); invalid.SystemPrompt += "！";
            var error = Expect<ArgumentException>(() => store.Save(invalid));
            Assert(error.Message.Contains("16000") && writes == 1 && store.Current.SystemPrompt == valid.SystemPrompt, "invalid prompt saved or configuration replaced");
        });
        await Check("agent sends exactly the saved editable prompt through all tool rounds", async () =>
        {
            var store = new ConnectionSettingsStore(() => null, _ => { });
            const string custom = "自定义女仆身份\n请称呼用户为主人，语气可爱。";
            store.Save(new ConnectionOptions { SystemPrompt = custom });
            var options = store.Current; int round = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(messages.Count(m => (string)m["role"] == "system") == 1 && (string)messages[0]["content"] == custom, "custom prompt replaced or hidden default appended");
                if (round++ == 0)
                {
                    options.SystemPrompt = "a later draft must not change an in-flight request";
                    return Call("prompt-query", "list_containers", new JObject());
                }
                return new ChatTurn { Text = "好哒，主人！" };
            });
            var agent = new HandsAgent(transport, ToolRegistry.ForHands(new HandsService(new FakeHands())));
            var result = await agent.RunAsync(options, "查看容器", null, default);
            Assert(round == 2 && result.ToolCalls == 1, "tool round did not complete");
        });
        await Check("changing the prompt clears old persona history on the next message", async () =>
        {
            var memory = new ConversationMemory();
            var options = new ConnectionOptions { SystemPrompt = "旧设定" }; int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (round++ == 0) return new ChatTurn { Text = "旧设定的回复。" };
                Assert(messages.Count == 2 && (string)messages[0]["content"] == AssistantPrompt.Default, "new prompt kept old history or was not used");
                Assert((string)messages[1]["content"] == "再打个招呼", "current message lost");
                return new ChatTurn { Text = "主人，小鲸来啦！" };
            });
            var agent = new HandsAgent(transport, new HandsService(new FakeHands()));
            await agent.RunAsync(options, "打个招呼", null, default, memory);
            options.SystemPrompt = ""; // Same normalization path as clearing the editor and saving.
            await agent.RunAsync(options, "再打个招呼", null, default, memory);
            Assert(memory.TurnCount == 1, "old persona exchange retained");
        });
    }
}
