using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI;
using ProbablyAI.Customers;
using ProbablyAI.Documents;
using ProbablyAI.Hands;
using ProbablyAI.Inspection;
using ProbablyAI.Localization;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunLocalizationTests()
    {
        try
        {
            await Check("game locale selects Chinese or English with reversible English fallback", () =>
            {
                Text.UseLocale("zh-CN"); Assert(Text.T("发送") == "发送", "Chinese unavailable");
                Assert(Text.UseLocale("en"), "English language change not detected");
                Assert(Text.T("发送") == "Send" && Branding.DisplayName == "Deep Space AI: Probably DeepSeek", "English UI/title unavailable");
                Text.UseLocale("de"); Assert(Text.IsEnglish && Text.T("关闭") == "Close", "unsupported language lacks English fallback");
                Assert(!Text.UseLocale("") && Text.IsEnglish, "initializing locale reset selection");
                Text.UseLocale("zh-TW"); Assert(!Text.IsEnglish && Text.T("发送") == "发送", "Chinese did not return");
            });
            await Check("all English catalog entries have translations and compatible format placeholders", () =>
            {
                Assert(EnglishTranslations.Values.Count >= 280, "catalog incomplete");
                foreach (var pair in EnglishTranslations.Values)
                {
                    Assert(!string.IsNullOrWhiteSpace(pair.Value) && !Regex.IsMatch(pair.Value, "[一-鿿]"), "untranslated English entry: " + pair.Key);
                    var slots = Regex.Matches(pair.Key, @"\{\d+[^}]*\}").Cast<Match>().Select(m => m.Value);
                    var translatedSlots = Regex.Matches(pair.Value, @"\{\d+[^}]*\}").Cast<Match>().Select(m => m.Value);
                    Assert(slots.SequenceEqual(translatedSlots), "translation changed format arguments: " + pair.Key);
                    string.Format(CultureInfo.InvariantCulture, pair.Value, Enumerable.Repeat<object>(1, 8).ToArray());
                }
            });
            await Check("saved default prompts follow game language and custom prompts remain unchanged", () =>
            {
                Text.UseLocale("zh"); string persisted = null;
                var store = new ConnectionSettingsStore(() => null, value => persisted = value);
                store.Save(new ConnectionOptions());
                var memory = new ConversationMemory(); memory.BindConnection(store.Current);
                memory.Commit(new JArray(new JObject { ["role"] = "user", ["content"] = "old language" }));
                Text.UseLocale("en");
                Assert(store.Current.SystemPrompt == AssistantPrompt.EnglishDefault && memory.BindConnection(store.Current) && memory.TurnCount == 0, "default or memory ignored language change");
                var reload = new ConnectionSettingsStore(() => persisted, _ => { });
                Assert(reload.Current.SystemPrompt == AssistantPrompt.EnglishDefault, "saved Chinese default did not adapt");
                Assert(AssistantPrompt.Default.Contains("Master") && AssistantPrompt.Default.Contains("do not use kaomoji, emoticons or emoji"), "English maid persona lost");
                store.Save(new ConnectionOptions { SystemPrompt = "My custom prompt\n我的自定义内容" });
                Text.UseLocale("zh"); Assert(store.Current.SystemPrompt == "My custom prompt\n我的自定义内容", "custom prompt translated or replaced");
            });
            await Check("existing tool registry descriptions follow language without changing names or parameters", () =>
            {
                Text.UseLocale("zh");
                var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
                new CustomerService(new FakeCustomer()).Register(registry);
                new InspectionService(new GameInspectionBackend(InspectionWorld())).Register(registry);
                new DocumentService(new FakeDocument()).Register(registry);
                var chinese = registry.Definitions();
                Text.UseLocale("en"); var english = registry.Definitions();
                Assert(english.Count == 16 && !Regex.IsMatch(english.ToString(), "[一-鿿]"), "English tool schemas contain untranslated descriptions");
                for (int i = 0; i < english.Count; i++)
                    Assert((string)english[i]["function"]["name"] == (string)chinese[i]["function"]["name"] &&
                        JToken.DeepEquals(english[i]["function"]["parameters"]["required"], chinese[i]["function"]["parameters"]["required"]), "translation changed protocol IDs");
                Text.UseLocale("zh"); Assert(JToken.DeepEquals(registry.Definitions(), chinese), "registry stored a translated snapshot");
            });
            await Check("English agent and validation use English while preserving real item IDs and user data", async () =>
            {
                Text.UseLocale("en"); var backend = FoodWorld();
                var hands = new HandsService(backend);
                var result = hands.Execute("find_items", new JObject { ["item_type"] = "FOOD", ["owned_only"] = true, ["container_id"] = "inventory" });
                Assert(result.Message == "Query succeeded." && ((JArray)Doc(result)["data"]["page"]["entries"]).Count == 2, "English query broke food classification");
                Assert(hands.Execute("list_container", new JObject { ["container_id"] = "gone" }).Message.StartsWith("This container ID"), "English validation unavailable");
                var error = Expect<ArgumentException>(() => new ConnectionOptions { BaseUrl = "bad" }.Normalized());
                Assert(error.Message.Contains("valid HTTP"), "English connection error unavailable");
                var transport = new FakeTransport((messages, token) =>
                {
                    Assert((string)messages[0]["content"] == AssistantPrompt.EnglishDefault, "English default not sent");
                    Assert((string)messages.Last["content"] == "Keep my wording 原样", "user message translated");
                    return new ChatTurn { Text = "Of course, Master!" };
                });
                await new HandsAgent(transport, hands).RunAsync(new ConnectionOptions(), "Keep my wording 原样", null, default);
            });
        }
        finally { Text.UseLocale("zh"); }
    }
}
