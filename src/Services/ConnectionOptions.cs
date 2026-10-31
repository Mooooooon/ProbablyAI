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

        public string BaseUrl { get; set; } = DefaultBaseUrl;
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = DefaultModel;
        public string ThinkingLevel { get; set; } = ThinkingLevels.Default;
        public string SystemPrompt { get; set; } = AssistantPrompt.Default;
        public bool RememberApiKey { get; set; } = true;

        public ConnectionOptions Copy() => new ConnectionOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Model = Model,
            ThinkingLevel = ThinkingLevel,
            SystemPrompt = SystemPrompt,
            RememberApiKey = RememberApiKey
        };

        public ConnectionOptions Normalized(bool requireModel = true)
        {
            var copy = Copy();
            copy.BaseUrl = (copy.BaseUrl ?? "").Trim().TrimEnd('/');
            copy.ApiKey = (copy.ApiKey ?? "").Trim();
            copy.Model = (copy.Model ?? "").Trim();
            copy.ThinkingLevel = ThinkingLevels.Normalize(copy.ThinkingLevel);
            copy.SystemPrompt = AssistantPrompt.Normalize(copy.SystemPrompt);
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
