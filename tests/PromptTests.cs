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
            string previousDefault = "你是『深空AI：可能是DeepSeek』中的女仆助手小鲸，在当铺里陪伴主人、帮主人打理物品与接待客人。称呼用户为“主人”，用中文交流，语气活泼、温柔可爱，可以适度使用“好哒”“交给小鲸吧”和少量颜文字。保持自然，不要每句话都堆叠卖萌；汇报操作、预算和失败原因时要简洁清楚。信息不足时明确说明，不编造游戏数值或执行结果。\n\n" +
                AssistantPrompt.PreviousChineseDefault.Substring(AssistantPrompt.PreviousChineseDefault.IndexOf("\n\n", StringComparison.Ordinal) + 2);
            var savedDefault = new ConnectionSettingsStore(() => new JObject { ["SystemPrompt"] = previousDefault }.ToString(), _ => { });
            Assert(savedDefault.Current.SystemPrompt == AssistantPrompt.Default, "saved old default was not upgraded");
            var customized = new ConnectionSettingsStore(() => new JObject { ["SystemPrompt"] = previousDefault + "\n自定义补充。" }.ToString(), _ => { });
            Assert(customized.Current.SystemPrompt == previousDefault + "\n自定义补充。", "customized old prompt was overwritten");
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
        await Check("task-focused prompts finish the job without closing questions", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("主人的任务优先") &&
                AssistantPrompt.ChineseDefault.Contains("不要在结尾反问") &&
                AssistantPrompt.ChineseDefault.Contains("已经完成的结果不必再次征求同意。"), "Chinese task guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("Master's task comes first") &&
                AssistantPrompt.EnglishDefault.Contains("Do not end with a question") &&
                AssistantPrompt.EnglishDefault.Contains("do not ask permission for results that are already complete."), "English task guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseTaskDefault, AssistantPrompt.PreviousEnglishTaskDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "task-focused default did not upgrade");
        });
        await Check("wanted guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("wanted") &&
                AssistantPrompt.ChineseDefault.Contains("（小声）主人，这个人符合通缉令上的特征呢。") &&
                AssistantPrompt.ChineseDefault.Contains("改造体") &&
                AssistantPrompt.ChineseDefault.Contains("不要自动举报"), "Chinese wanted guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("wanted.quiet_reminder") &&
                AssistantPrompt.EnglishDefault.Contains("augmented human") &&
                AssistantPrompt.EnglishDefault.Contains("do not report, arrest"), "English wanted guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseWantedDefault, AssistantPrompt.PreviousEnglishWantedDefault,
                AssistantPrompt.PreviousChineseAugDefault, AssistantPrompt.PreviousEnglishAugDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "wanted default did not upgrade");
        });
        await Check("document reading and machine-source guidance are built into prompts", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("read_document") &&
                AssistantPrompt.ChineseDefault.Contains("生产设备输入区") &&
                AssistantPrompt.ChineseDefault.Contains("材料区除非主人明确要求") &&
                AssistantPrompt.ChineseDefault.Contains("设备中正在供电或充电的电池") &&
                AssistantPrompt.ChineseDefault.Contains("in_use=true") &&
                AssistantPrompt.ChineseDefault.Contains("include_in_use=true"), "Chinese document/source guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("read_document") &&
                AssistantPrompt.EnglishDefault.Contains("machine input compartments") &&
                AssistantPrompt.EnglishDefault.Contains("unless Master explicitly asks") &&
                AssistantPrompt.EnglishDefault.Contains("batteries that are powering or charging equipment") &&
                AssistantPrompt.EnglishDefault.Contains("include_in_use=true"), "English document/source guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseDocumentDefault, AssistantPrompt.PreviousEnglishDocumentDefault,
                AssistantPrompt.PreviousChineseBatteryDefault, AssistantPrompt.PreviousEnglishBatteryDefault,
                AssistantPrompt.PreviousChineseChargingDefault, AssistantPrompt.PreviousEnglishChargingDefault,
                AssistantPrompt.PreviousChinesePinnedDefault, AssistantPrompt.PreviousEnglishPinnedDefault,
                AssistantPrompt.PreviousChineseRecorderDefault, AssistantPrompt.PreviousEnglishRecorderDefault,
                AssistantPrompt.PreviousChineseStolenDefault, AssistantPrompt.PreviousEnglishStolenDefault,
                AssistantPrompt.PreviousChineseRibwichDefault, AssistantPrompt.PreviousEnglishRibwichDefault,
                AssistantPrompt.PreviousChinesePlasmaDefault, AssistantPrompt.PreviousEnglishPlasmaDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "document default did not upgrade");
        });
        await Check("pinned goods guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("不要选择主人已固定的物品") &&
                AssistantPrompt.ChineseDefault.Contains("pinned=true") &&
                AssistantPrompt.ChineseDefault.Contains("include_pinned=true"), "Chinese pinned guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("items Master has pinned") &&
                AssistantPrompt.EnglishDefault.Contains("pinned=true") &&
                AssistantPrompt.EnglishDefault.Contains("include_pinned=true"), "English pinned guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChinesePinnedDefault, AssistantPrompt.PreviousEnglishPinnedDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "pinned default did not upgrade");
        });
        await Check("recorder dialogue guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("dialogue_history") &&
                AssistantPrompt.ChineseDefault.Contains("录音机") &&
                AssistantPrompt.ChineseDefault.Contains("只使用客人的行"), "Chinese recorder guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("dialogue_history") &&
                AssistantPrompt.EnglishDefault.Contains("recorder") &&
                AssistantPrompt.EnglishDefault.Contains("quote only the customer's lines"), "English recorder guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseRecorderDefault, AssistantPrompt.PreviousEnglishRecorderDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "recorder default did not upgrade");
        });
        await Check("stolen goods guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("默认不要选择赃物") &&
                AssistantPrompt.ChineseDefault.Contains("stolen=true") &&
                AssistantPrompt.ChineseDefault.Contains("include_stolen=true") &&
                AssistantPrompt.ChineseDefault.Contains("heat"), "Chinese stolen guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("do not select stolen items by default") &&
                AssistantPrompt.EnglishDefault.Contains("stolen=true") &&
                AssistantPrompt.EnglishDefault.Contains("include_stolen=true") &&
                AssistantPrompt.EnglishDefault.Contains("heat"), "English stolen guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseStolenDefault, AssistantPrompt.PreviousEnglishStolenDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "stolen default did not upgrade");
        });
        await Check("ribwich appraisal guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("appraise_ribwich") &&
                AssistantPrompt.ChineseDefault.Contains("肋明治") &&
                AssistantPrompt.ChineseDefault.Contains("base_value") &&
                AssistantPrompt.ChineseDefault.Contains("opened=true"), "Chinese ribwich guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("appraise_ribwich") &&
                AssistantPrompt.EnglishDefault.Contains("Packaged Rib Sandwich") &&
                AssistantPrompt.EnglishDefault.Contains("base_value") &&
                AssistantPrompt.EnglishDefault.Contains("opened=true"), "English ribwich guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseRibwichDefault, AssistantPrompt.PreviousEnglishRibwichDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "ribwich default did not upgrade");
        });
        await Check("plasma fuel appraisal guidance is built into prompts and old defaults upgrade", () =>
        {
            Assert(AssistantPrompt.ChineseDefault.Contains("appraise_plasma_fuel") &&
                AssistantPrompt.ChineseDefault.Contains("等离子燃料匣") &&
                AssistantPrompt.ChineseDefault.Contains("base_value") &&
                AssistantPrompt.ChineseDefault.Contains("没有真伪判定"), "Chinese plasma guidance missing");
            Assert(AssistantPrompt.EnglishDefault.Contains("appraise_plasma_fuel") &&
                AssistantPrompt.EnglishDefault.Contains("Plasma Cartridge") &&
                AssistantPrompt.EnglishDefault.Contains("base_value") &&
                AssistantPrompt.EnglishDefault.Contains("no authenticity verdict"), "English plasma guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChinesePlasmaDefault, AssistantPrompt.PreviousEnglishPlasmaDefault })
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "plasma default did not upgrade");
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
