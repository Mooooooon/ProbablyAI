using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Services;

internal static partial class Program
{
    private static int passed;
    private static int failed;

    private static async Task<int> Main()
    {
        await Check("defaults and root/versioned/custom endpoint prefixes", () =>
        {
            var options = new ConnectionOptions();
            Assert(options.BaseUrl == "https://api.deepseek.com" && options.Model == "deepseek-flash", "defaults changed");
            Assert(options.Endpoint("models").AbsoluteUri == "https://api.deepseek.com/models", "root endpoint");
            options.BaseUrl = " https://example.test/proxy/v1/// ";
            Assert(options.Endpoint("chat/completions").AbsoluteUri == "https://example.test/proxy/v1/chat/completions", "prefix duplicated/lost");
            options.BaseUrl = "http://127.0.0.1:1234/v1";
            Assert(options.Endpoint("models").AbsoluteUri == "http://127.0.0.1:1234/v1/models", "local endpoint");
        });
        await Check("invalid endpoint, credentials, query, fragment and key rejected", () =>
        {
            foreach (var url in new[] { "", "ftp://example.test", "https://name:pass@example.test", "https://example.test?key=abc", "https://example.test/#fragment" })
                Expect<ArgumentException>(() => new ConnectionOptions { BaseUrl = url }.Normalized());
            Expect<ArgumentException>(() => new ConnectionOptions { ApiKey = "abc\r\nInjected: secret" }.Normalized());
            Expect<ArgumentException>(() => new ConnectionOptions { Model = " " }.Normalized());
            new ConnectionOptions { Model = "" }.Normalized(false);
        });
        await Check("missing and partial configuration preserve defaults", () =>
        {
            var missing = new ConnectionSettingsStore(() => null, _ => { });
            Assert(missing.Current.Model == ConnectionOptions.DefaultModel, "missing config");
            var partial = new ConnectionSettingsStore(() => "{\"ApiKey\":\"test-only\"}", _ => { });
            Assert(partial.Current.BaseUrl == ConnectionOptions.DefaultBaseUrl, "partial config");
        });
        await Check("corrupt configuration gives safe fallback", () =>
        {
            var store = new ConnectionSettingsStore(() => "not-json-test-only", _ => { });
            Assert(store.LoadWarning != null && store.Current.ApiKey == "", "bad config not handled");
        });
        await Check("remembered key and selection survive a reload", () =>
        {
            string persisted = null;
            var store = new ConnectionSettingsStore(() => null, value => persisted = value);
            store.Save(new ConnectionOptions { ApiKey = "test-only", Model = "custom-model" });
            var reloaded = new ConnectionSettingsStore(() => persisted, _ => { });
            Assert(reloaded.Current.ApiKey == "test-only" && reloaded.Current.Model == "custom-model", "roundtrip");
        });
        await Check("session-only key is omitted from disk and survives only in memory", () =>
        {
            string persisted = null;
            var store = new ConnectionSettingsStore(() => null, value => persisted = value);
            store.Save(new ConnectionOptions { ApiKey = "test-only-secret", RememberApiKey = false });
            Assert(!persisted.Contains("test-only-secret") && store.Current.ApiKey == "test-only-secret", "key storage policy");
            var reloaded = new ConnectionSettingsStore(() => persisted, _ => { });
            Assert(reloaded.Current.ApiKey == "", "key persisted unexpectedly");
            var stale = new ConnectionSettingsStore(() => "{\"RememberApiKey\":false,\"ApiKey\":\"stale-key\"}", _ => { });
            Assert(stale.Current.ApiKey == "", "stale stored key used");
        });
        await Check("failed save does not replace active configuration or echo secrets", () =>
        {
            var store = new ConnectionSettingsStore(() => null, _ => throw new Exception("test-only-secret"));
            var error = Expect<InvalidOperationException>(() => store.Save(new ConnectionOptions { Model = "new", ApiKey = "test-only-secret" }));
            Assert(store.Current.Model == ConnectionOptions.DefaultModel && !error.ToString().Contains("test-only-secret"), "failed save leaked/mutated");
            var copy = store.Current;
            copy.Model = "changed";
            Assert(store.Current.Model == ConnectionOptions.DefaultModel, "live config exposed");
        });
        await Check("models request uses GET, bearer auth and sorts/deduplicates IDs", async () =>
        {
            using var http = new HttpClient(new FakeHandler((request, token) =>
            {
                Assert(request.Method == HttpMethod.Get && request.Content == null, "wrong method/body");
                Assert(request.RequestUri.AbsoluteUri == "https://example.test/v1/models", "wrong list route");
                Assert(request.Headers.Authorization?.ToString() == "Bearer test-only", "missing auth");
                return Task.FromResult(Json("{\"data\":[{\"id\":\"z\"},{\"id\":\"a\",\"context_window\":100},{\"id\":\"a\"},{\"id\":4},{\"id\":\" \"},null]}"));
            }));
            using var client = new OpenAiApiClient(http);
            var models = await client.ListModelsAsync(new ConnectionOptions { BaseUrl = "https://example.test/v1", ApiKey = "test-only", Model = "" }, default);
            Assert(models.SequenceEqual(new[] { "a", "z" }), "model parsing");
        });
        await Check("anonymous local endpoint and empty list are supported", async () =>
        {
            using var http = new HttpClient(new FakeHandler((request, token) =>
            {
                Assert(request.Headers.Authorization == null, "empty bearer sent");
                return Task.FromResult(Json("{\"data\":[]}"));
            }));
            using var client = new OpenAiApiClient(http);
            Assert((await client.ListModelsAsync(new ConnectionOptions(), default)).Count == 0, "empty list");
        });
        await Check("non-JSON and nonstandard model responses give actionable errors", async () =>
        {
            foreach (var payload in new[] { "<html>not an API</html>", "{}", "[]" })
            {
                using var http = Respond(payload);
                using var client = new OpenAiApiClient(http);
                await ExpectAsync<ApiRequestException>(() => client.ListModelsAsync(new ConnectionOptions(), default));
            }
        });
        await Check("HTTP errors are safe and list failure does not replace a custom model", async () =>
        {
            foreach (var status in new[] { 400, 401, 402, 403, 404, 429, 500, 503, 302 })
            {
                var options = new ConnectionOptions { Model = "my-model", ApiKey = "test-only-secret" };
                using var http = Respond("{\"error\":{\"message\":\"test-only-secret\"}}", status);
                using var client = new OpenAiApiClient(http);
                var error = await ExpectAsync<ApiRequestException>(() => client.ListModelsAsync(options, default));
                Assert(!error.ToString().Contains("test-only-secret") && options.Model == "my-model", "error leaked or changed model");
            }
        });
        await Check("chat payload uses the chosen model and preserves Unicode/message text", async () =>
        {
            using var http = new HttpClient(new FakeHandler(async (request, token) =>
            {
                Assert(request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/v1/chat/completions", "chat route");
                Assert(request.Content.Headers.ContentType.MediaType == "application/json", "chat content type");
                var body = JObject.Parse(await request.Content.ReadAsStringAsync());
                Assert((string)body["model"] == "custom-flash" && !(bool)body["stream"], "chat parameters");
                Assert((string)body["messages"][0]["role"] == "system", "system message");
                Assert((string)body["messages"][1]["content"] == "你好\n\"小鲸\"", "message mangled");
                Assert(!body.ToString().Contains("test-only-secret"), "key in payload");
                return Json("{\"choices\":[{\"message\":{\"content\":\"连接成功\"}}]}");
            }));
            using var client = new OpenAiApiClient(http);
            var reply = await client.CompleteAsync(new ConnectionOptions { BaseUrl = "https://example.test/v1", Model = "custom-flash", ApiKey = "test-only-secret" }, "你好\n\"小鲸\"", "system", default);
            Assert(reply == "连接成功", "reply parsing");
        });
        await Check("multipart text reply is supported", async () =>
        {
            using var http = Respond("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"你\"},{\"type\":\"text\",\"text\":\"好\"}]}}]}");
            using var client = new OpenAiApiClient(http);
            Assert(await client.CompleteAsync(new ConnectionOptions(), "hello", "system", default) == "你好", "multipart reply");
        });
        await Check("missing reply and reasoning-only response do not claim success", async () =>
        {
            foreach (var payload in new[] { "{}", "{\"choices\":[]}", "{\"choices\":[{\"message\":{\"content\":null,\"reasoning_content\":\"thinking\"}}]}" })
            {
                using var http = Respond(payload);
                using var client = new OpenAiApiClient(http);
                await ExpectAsync<ApiRequestException>(() => client.CompleteAsync(new ConnectionOptions(), "hello", "system", default));
            }
        });
        await Check("constructing a client never starts a request; validation avoids network", async () =>
        {
            var calls = 0;
            using var http = new HttpClient(new FakeHandler((request, token) => { calls++; return Task.FromResult(Json("{}")); }));
            using var client = new OpenAiApiClient(http);
            Assert(calls == 0, "automatic request");
            await ExpectAsync<ArgumentException>(() => client.CompleteAsync(new ConnectionOptions(), " ", "system", default));
            await ExpectAsync<ArgumentException>(() => client.ListModelsAsync(new ConnectionOptions { BaseUrl = "bad-url" }, default));
            Assert(calls == 0, "validation sent a request");
        });
        await Check("per-request credentials do not leak between providers", async () =>
        {
            var observed = new List<string>();
            using var http = new HttpClient(new FakeHandler((request, token) =>
            {
                observed.Add(request.Headers.Authorization?.Parameter ?? "");
                return Task.FromResult(Json("{\"data\":[]}"));
            }));
            using var client = new OpenAiApiClient(http);
            await client.ListModelsAsync(new ConnectionOptions { ApiKey = "key-a" }, default);
            await client.ListModelsAsync(new ConnectionOptions { BaseUrl = "https://other.test/v1", ApiKey = "key-b" }, default);
            await client.ListModelsAsync(new ConnectionOptions { ApiKey = "" }, default);
            Assert(observed.SequenceEqual(new[] { "key-a", "key-b", "" }), "auth leaked");
        });
        await Check("network failures do not expose exception/header secrets", async () =>
        {
            using var http = new HttpClient(new FakeHandler((request, token) => throw new HttpRequestException("test-only-secret")));
            using var client = new OpenAiApiClient(http);
            var error = await ExpectAsync<ApiRequestException>(() => client.ListModelsAsync(new ConnectionOptions(), default));
            Assert(!error.ToString().Contains("test-only-secret"), "network exception leak");
        });
        await Check("cancel interrupts an in-flight request", async () =>
        {
            using var http = new HttpClient(new FakeHandler(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Json("{}");
            }));
            using var client = new OpenAiApiClient(http);
            using var cancellation = new CancellationTokenSource();
            var pending = client.ListModelsAsync(new ConnectionOptions(), cancellation.Token);
            cancellation.Cancel();
            await ExpectAsync<OperationCanceledException>(() => pending);
        });
        await Check("request timeout is distinguished from user cancellation", async () =>
        {
            using var http = new HttpClient(new FakeHandler(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Json("{}");
            }));
            using var client = new OpenAiApiClient(http, TimeSpan.FromMilliseconds(40));
            var error = await ExpectAsync<ApiRequestException>(() => client.ListModelsAsync(new ConnectionOptions(), default));
            Assert(error.Message.Contains("超时"), "timeout message");
        });
        await RunHandsTests();
        await RunChatTests();
        await RunCustomerTests();
        Console.WriteLine($"{passed} passed; {failed} failed. Game state and HTTP responses were simulated; no real save, API key or external endpoint was used.");
        return failed == 0 ? 0 : 1;
    }

    private static HttpClient Respond(string payload, int status = 200)
        => new HttpClient(new FakeHandler((_, _) => Task.FromResult(Json(payload, status))));

    private static HttpResponseMessage Json(string payload, int status = 200)
        => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(payload) };

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task<T> ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static Task Check(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });

    private static async Task Check(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + " " + error.Message); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) => this.handle = handle;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
