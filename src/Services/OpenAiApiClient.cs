using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    public sealed class ApiRequestException : Exception
    {
        public ApiRequestException(string message) : base(message) { }
    }

    public sealed class OpenAiApiClient : IDisposable, IChatTransport
    {
        private readonly HttpClient http;
        private readonly bool ownsHttp;
        private readonly TimeSpan timeout;

        public OpenAiApiClient(HttpClient http = null, TimeSpan? timeout = null)
        {
            ownsHttp = http == null;
            this.http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 2 * 1024 * 1024
            };
            this.timeout = timeout ?? TimeSpan.FromSeconds(60);
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(ConnectionOptions options, CancellationToken cancellationToken)
        {
            var connection = options.Normalized(false);
            var body = await SendAsync(connection, HttpMethod.Get, "models", null, cancellationToken).ConfigureAwait(false);
            var document = ParseObject(body);
            if (!(document["data"] is JArray data))
                throw new ApiRequestException(T("模型列表格式不兼容：响应中缺少 data 数组。仍可手动输入模型 ID。"));
            return data.OfType<JObject>()
                .Where(item => item["id"]?.Type == JTokenType.String)
                .Select(item => ((string)item["id"]).Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        public async Task<string> CompleteAsync(ConnectionOptions options, string message, string systemPrompt, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException(T("请先输入测试消息。"));
            var messages = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = systemPrompt },
                new JObject { ["role"] = "user", ["content"] = message }
            };
            var turn = await CompleteTurnAsync(options, messages, null, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(turn.Text))
                throw new ApiRequestException(T("服务未返回可显示的文本回复，请确认所选模型支持 Chat Completions。"));
            return turn.Text;
        }

        public async Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken cancellationToken)
        {
            var connection = options.Normalized();
            var payload = new JObject { ["model"] = connection.Model, ["messages"] = messages.DeepClone(), ["stream"] = false };
            if (tools != null) { payload["tools"] = tools.DeepClone(); payload["tool_choice"] = "auto"; }
            ThinkingLevels.Apply(payload, connection.ThinkingLevel);
            var body = await SendAsync(connection, HttpMethod.Post, "chat/completions", payload.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
            var choices = ParseObject(body)["choices"] as JArray;
            var raw = (choices?.FirstOrDefault() as JObject)?["message"] as JObject;
            if (raw == null) throw new ApiRequestException(T("服务未返回有效的助手消息。"));
            var content = raw["content"];
            string reply = null;
            if (content?.Type == JTokenType.String) reply = (string)content;
            else if (content is JArray parts)
                reply = string.Concat(parts.OfType<JObject>()
                    .Where(part => part["text"]?.Type == JTokenType.String)
                    .Select(part => (string)part["text"]));
            var calls = new List<ChatToolCall>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (raw["tool_calls"] != null && raw["tool_calls"].Type != JTokenType.Null && !(raw["tool_calls"] is JArray))
                throw new ApiRequestException(T("模型返回了无效的工具调用列表，已停止。"));
            if (raw["tool_calls"] is JArray toolCalls)
            {
                if (toolCalls.Count > 16) throw new ApiRequestException(T("模型单次请求的工具操作过多，已停止。"));
                foreach (var token in toolCalls)
                {
                    var call = token as JObject;
                    var function = call?["function"] as JObject;
                    if (call?["type"]?.Type != JTokenType.String || (string)call["type"] != "function" ||
                        call["id"]?.Type != JTokenType.String || function?["name"]?.Type != JTokenType.String || function["arguments"]?.Type != JTokenType.String)
                        throw new ApiRequestException(T("模型返回了无效的工具调用，已停止。"));
                    var id = (string)call["id"];
                    var name = (string)function["name"];
                    var arguments = (string)function["arguments"];
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || !ids.Add(id) ||
                        string.IsNullOrWhiteSpace(name) || name.Length > 64 || arguments.Length > 16000)
                        throw new ApiRequestException(T("模型返回了重复或超出限制的工具调用，已停止。"));
                    calls.Add(new ChatToolCall { Id = id, Name = name, Arguments = arguments });
                }
            }
            if (calls.Count == 0 && string.IsNullOrWhiteSpace(reply))
                throw new ApiRequestException(T("服务未返回可显示的文本回复，请确认所选模型支持 Chat Completions。"));
            var message = (JObject)raw.DeepClone();
            message["role"] = "assistant";
            return new ChatTurn { Message = message, Text = reply, ToolCalls = calls };
        }

        private async Task<string> SendAsync(ConnectionOptions options, HttpMethod method, string route, string body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(method, options.Endpoint(route)))
            {
                timeoutSource.CancelAfter(timeout);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrEmpty(options.ApiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new ApiRequestException(StatusMessage((int)response.StatusCode));
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ApiRequestException(T("请求超时，请稍后重试或检查服务地址。"));
                }
                catch (HttpRequestException)
                {
                    // Do not surface provider bodies, request URLs or headers: they can contain a key.
                    throw new ApiRequestException(T("无法连接服务，请检查网络、服务地址和 HTTPS 证书。"));
                }
            }
        }

        private static JObject ParseObject(string body)
        {
            try { return JObject.Parse(body); }
            catch (JsonException) { throw new ApiRequestException(T("服务返回的内容不是有效的 JSON，请检查服务地址。")); }
        }

        private static string StatusMessage(int status)
        {
            switch (status)
            {
                case 400: return T("请求被拒绝（HTTP 400），请检查模型 ID 和接口格式。");
                case 401: return T("认证失败（HTTP 401），请检查 API Key。");
                case 402: return T("余额不足或需要付费（HTTP 402），请检查服务账户。");
                case 403: return T("无访问权限（HTTP 403），请检查密钥权限和服务限制。");
                case 404: return T("接口不存在（HTTP 404），请检查服务地址是否需要 /v1；也可手动填写模型。");
                case 429: return T("请求过于频繁或额度不足（HTTP 429），请稍后重试。");
                default:
                    if (status >= 300 && status < 400) return T("服务返回重定向，请填写最终的 API 服务地址。");
                    if (status >= 500) return F("服务暂时不可用（HTTP {0}），请稍后重试。", status);
                    return F("请求失败（HTTP {0}），请检查连接配置。", status);
            }
        }

        public void Dispose() { if (ownsHttp) http.Dispose(); }
    }
}
