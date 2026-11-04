using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    public static class ThinkingLevels
    {
        public const string Default = "default";
        public const string Disabled = "disabled";
        public const string Low = "low";
        public const string High = "high";
        public const string Max = "max";

        // Order matches the settings dropdown.
        public static readonly string[] All = { Default, Disabled, Low, High, Max };

        public static string Normalize(string value)
        {
            string level = (value ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(All, level) >= 0 ? level : Default;
        }

        // Only send provider reasoning fields for an explicitly chosen level, so older services keep working.
        public static void Apply(JObject payload, string level)
        {
            switch (Normalize(level))
            {
                case Disabled:
                    payload["thinking"] = new JObject { ["type"] = "disabled" };
                    break;
                case Low:
                case High:
                case Max:
                    payload["thinking"] = new JObject { ["type"] = "enabled" };
                    payload["reasoning_effort"] = level;
                    break;
            }
        }
    }

    public sealed class QuickReply
    {
        public const int SlotCount = 5;
        public const int MaxLabelLength = 40;
        public const int MaxMessageLength = 6000;

        public string Label { get; }
        public string Message { get; }
        [JsonIgnore]
        public bool IsEnabled => Label.Length > 0 && Message.Length > 0;

        public QuickReply(string label, string message)
        {
            Label = (label ?? "").Trim();
            Message = (message ?? "").Trim();
        }

        public static QuickReply DefaultForSlot(int slot)
        {
            switch (slot)
            {
                case 0:
                    return new QuickReply(T("按照客户需求挑选商品"), T("按照客户需求挑选商品，直接摆到柜台上。"));
                case 1:
                    return new QuickReply(T("帮我鉴定对方带来的东西"), T("帮我鉴定对方带来的东西，逐件告诉我结果。"));
                case 2:
                    return new QuickReply(T("分批净水（2 批）"), T(
                        "请按以下流程处理 2 批水，每批 3000 mL。先确认水龙头已解锁，备好 3000 mL 水罐、容量足够的空水罐、有剩余耐久的过滤器、紫外线灯和净水药片。" +
                        "\n1. 给 3000 mL 水罐装上有剩余耐久的过滤器，到水龙头接满水。" +
                        "\n2. 重新检查过滤器耐久。还有耐久就继续。耐久为零时，先把水倒入空水罐暂存，换上有耐久的过滤器，再把暂存的水倒回原罐。" +
                        "\n3. 用紫外线灯照射原罐里的水，再依次投入两片净水药片。" +
                        "\n4. 把预处理过的水倒入机器区净水器里的大储水罐，核对实际倒入量，再处理下一批。" +
                        "\n材料不足、耐久读不到、储水罐容量不足或任一步失败时，停下并报告已完成批数、实际转存量和原因。"));
                default:
                    return new QuickReply("", "");
            }
        }
    }

    public sealed class ConnectionOptions
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com";
        public const string DefaultModel = "deepseek-flash";

        // History limits are per request and measured on the model transcript; the token
        // count is an estimate, not a tokenizer run.
        public const int DefaultHistoryTurns = 8;
        public const int HistoryTurnsMin = 1;
        public const int HistoryTurnsMax = 50;
        public const int DefaultHistoryTokens = 32000;
        public const int HistoryTokensMin = 1000;
        public const int HistoryTokensMax = 200000;
        public const int DefaultToolRounds = 16;
        public const int ToolRoundsMin = 1;
        public const int ToolRoundsMax = 1000;
        public const int DefaultToolCalls = 48;
        public const int ToolCallsMin = 1;
        public const int ToolCallsMax = 10000;

        public string BaseUrl { get; set; } = DefaultBaseUrl;
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = DefaultModel;
        public string ThinkingLevel { get; set; } = ThinkingLevels.Default;
        public string SystemPrompt { get; set; } = AssistantPrompt.Default;
        public string PlayerTips { get; set; } = "";
        // Null entries use the current locale's preset. Explicit empty replies hide their buttons.
        public QuickReply[] QuickReplies { get; set; }
        public bool RememberApiKey { get; set; } = true;
        public int MaxHistoryTurns { get; set; } = DefaultHistoryTurns;
        public int MaxHistoryTokens { get; set; } = DefaultHistoryTokens;
        public int MaxToolRounds { get; set; } = DefaultToolRounds;
        public int MaxToolCalls { get; set; } = DefaultToolCalls;

        public ConnectionOptions Copy() => new ConnectionOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Model = Model,
            ThinkingLevel = ThinkingLevel,
            SystemPrompt = SystemPrompt,
            PlayerTips = PlayerTips,
            QuickReplies = (QuickReply[])QuickReplies?.Clone(),
            RememberApiKey = RememberApiKey,
            MaxHistoryTurns = MaxHistoryTurns,
            MaxHistoryTokens = MaxHistoryTokens,
            MaxToolRounds = MaxToolRounds,
            MaxToolCalls = MaxToolCalls
        };

        public ConnectionOptions Normalized(bool requireModel = true)
        {
            var copy = Copy();
            copy.BaseUrl = (copy.BaseUrl ?? "").Trim().TrimEnd('/');
            copy.ApiKey = (copy.ApiKey ?? "").Trim();
            copy.Model = (copy.Model ?? "").Trim();
            copy.ThinkingLevel = ThinkingLevels.Normalize(copy.ThinkingLevel);
            copy.SystemPrompt = AssistantPrompt.Normalize(copy.SystemPrompt);
            copy.PlayerTips = (copy.PlayerTips ?? "").Trim();
            if (copy.PlayerTips.Length > AssistantPrompt.MaxLength)
                throw new ArgumentException(F("自定义游戏技巧不能超过 {0} 字。", AssistantPrompt.MaxLength));
            var replies = copy.QuickReplies;
            Array.Resize(ref replies, QuickReply.SlotCount);
            copy.QuickReplies = replies;
            for (int slot = 0; slot < replies.Length; slot++)
            {
                var reply = replies[slot];
                if (reply == null) continue;
                if (reply.Label.Length > QuickReply.MaxLabelLength || reply.Message.Length > QuickReply.MaxMessageLength)
                    throw new ArgumentException(F("快捷发言 {0} 的名称最多 {1} 字，内容最多 {2} 字。", slot + 1, QuickReply.MaxLabelLength, QuickReply.MaxMessageLength));
                if (reply.Label.Contains("\n") || reply.Label.Contains("\r") || (!reply.IsEnabled && (reply.Label.Length > 0 || reply.Message.Length > 0)))
                    throw new ArgumentException(F("快捷发言 {0} 需要单行名称和发送内容。清空两项可隐藏按钮。", slot + 1));
            }
            // Old configuration files gain the defaults; out-of-range values fall back safely.
            if (copy.MaxHistoryTurns < HistoryTurnsMin || copy.MaxHistoryTurns > HistoryTurnsMax)
                copy.MaxHistoryTurns = DefaultHistoryTurns;
            if (copy.MaxHistoryTokens < HistoryTokensMin || copy.MaxHistoryTokens > HistoryTokensMax)
                copy.MaxHistoryTokens = DefaultHistoryTokens;
            if (copy.MaxToolRounds < ToolRoundsMin || copy.MaxToolRounds > ToolRoundsMax)
                copy.MaxToolRounds = DefaultToolRounds;
            if (copy.MaxToolCalls < ToolCallsMin || copy.MaxToolCalls > ToolCallsMax)
                copy.MaxToolCalls = DefaultToolCalls;
            if (!Uri.TryCreate(copy.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException(T("请输入有效的 HTTP / HTTPS 服务地址。"));
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException(T("服务地址不能包含账号密码、查询参数或 #；API Key 请填在独立输入框中。"));
            if (copy.ApiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException(T("API Key 不能包含换行。"));
            if (requireModel && string.IsNullOrWhiteSpace(copy.Model))
                throw new ArgumentException(T("请输入模型 ID，或从获取到的模型列表中选择。"));
            return copy;
        }

        public string ComposeSystemPrompt() => string.IsNullOrEmpty(PlayerTips)
            ? SystemPrompt
            : SystemPrompt + "\n\n" + T("玩家自定义游戏技巧") + "\n" + PlayerTips;

        public QuickReply GetQuickReply(int slot)
            => (QuickReplies != null && slot >= 0 && slot < QuickReplies.Length ? QuickReplies[slot] : null)
                ?? QuickReply.DefaultForSlot(slot);

        // Append to the configured prefix exactly once: /v1 is optional and user-controlled.
        public Uri Endpoint(string route)
        {
            return new Uri(Normalized(false).BaseUrl + "/" + route.TrimStart('/'));
        }
    }
}
