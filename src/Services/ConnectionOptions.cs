using System;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    public sealed class ConnectionOptions
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com";
        public const string DefaultModel = "deepseek-flash";

        public string BaseUrl { get; set; } = DefaultBaseUrl;
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = DefaultModel;
        public string SystemPrompt { get; set; } = AssistantPrompt.Default;
        public bool RememberApiKey { get; set; } = true;

        public ConnectionOptions Copy() => new ConnectionOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Model = Model,
            SystemPrompt = SystemPrompt,
            RememberApiKey = RememberApiKey
        };

        public ConnectionOptions Normalized(bool requireModel = true)
        {
            var copy = Copy();
            copy.BaseUrl = (copy.BaseUrl ?? "").Trim().TrimEnd('/');
            copy.ApiKey = (copy.ApiKey ?? "").Trim();
            copy.Model = (copy.Model ?? "").Trim();
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
