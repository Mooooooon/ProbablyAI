using System;
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

        // Append to the configured prefix exactly once: /v1 is optional and user-controlled.
        public Uri Endpoint(string route)
        {
            return new Uri(Normalized(false).BaseUrl + "/" + route.TrimStart('/'));
        }
    }
}
