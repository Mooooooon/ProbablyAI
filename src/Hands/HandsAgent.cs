using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Machines;
using ProbablyAI.Macros;
using ProbablyAI.Services;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Hands
{
    public sealed class HandsRunResult
    {
        public string Reply { get; set; }
        public int ToolCalls { get; set; }
        public List<string> Operations { get; } = new List<string>();
    }

    public sealed class HandsAgent
    {
        private readonly IChatTransport transport;
        private readonly ToolRegistry registry;
        private readonly bool refreshShopContext;
        public bool IsRunning { get; private set; }

        public HandsAgent(IChatTransport transport, HandsService hands) : this(transport, ToolRegistry.ForHands(hands)) { }
        public HandsAgent(IChatTransport transport, ToolRegistry registry, bool refreshShopContext = false)
        { this.transport = transport; this.registry = registry; this.refreshShopContext = refreshShopContext; }

        internal async Task<HandsResult> RunLocalMacroAsync(MacroService macros, string id, long revision, JObject inputs,
            Action<string> progress, CancellationToken cancellationToken)
        {
            if (IsRunning) throw new InvalidOperationException(T("助手正在处理上一条消息，请等待完成或先停止。"));
            IsRunning = true;
            try { return await macros.RunAsync(id, inputs, cancellationToken, progress, revision); }
            finally { IsRunning = false; }
        }

        public async Task<HandsRunResult> RunAsync(ConnectionOptions options, string instruction, Action<string> progress, CancellationToken cancellationToken, ConversationMemory memory = null)
        {
            if (IsRunning) throw new InvalidOperationException(T("助手正在处理上一条消息，请等待完成或先停止。"));
            if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 6000)
                throw new ArgumentException(T("请输入 1–6000 字的消息。"));
            var connection = options.Normalized();
            var result = new HandsRunResult();
            var exchange = new JArray { new JObject { ["role"] = "user", ["content"] = instruction } };
            var outstanding = new List<ChatToolCall>();
            var callIds = new HashSet<string>();
            bool macroFailed = false;
            IsRunning = true;
            try
            {
                memory?.BindConnection(connection);
                var history = memory?.ContextMessages(connection.MaxHistoryTurns, connection.MaxHistoryTokens, registry.ChangesState);
                var prefix = new JArray { new JObject { ["role"] = "system", ["content"] = connection.ComposeSystemPrompt() } };
                if (history != null) foreach (var message in history) prefix.Add(message.DeepClone());
                if (refreshShopContext)
                {
                    var observation = ReadShopContext(result, cancellationToken);
                    if (observation != null) prefix.Add(observation);
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke(T("正在思考…"));
                    var messages = (JArray)prefix.DeepClone();
                    foreach (var message in exchange) messages.Add(message.DeepClone());
                    // Keep Unity's synchronization context so every registered tool runs on its main thread.
                    var turn = await transport.CompleteTurnAsync(connection, messages, registry.Definitions(), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (turn.ToolCalls.Count == 0)
                    {
                        result.Reply = turn.Text;
                        exchange.Add(turn.Message?.DeepClone() ?? new JObject { ["role"] = "assistant", ["content"] = turn.Text });
                        memory?.Commit(exchange);
                        return result;
                    }
                    foreach (var call in turn.ToolCalls)
                        if (!callIds.Add(call.Id)) throw new ApiRequestException(T("模型重复了调用 ID，已停止。请检查操作记录。"));
                    exchange.Add(turn.Message.DeepClone());
                    outstanding.AddRange(turn.ToolCalls);
                    bool machineStepFailed = false;
                    foreach (var call in turn.ToolCalls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.ToolCalls++;
                        HandsResult outcome;
                        if (macroFailed && (registry.ChangesState(call.Name) || call.Name == MacroService.SaveDraftTool))
                            outcome = HandsResult.Fail("not_executed", T("宏已停止，本次请求的后续写操作已跳过。"));
                        else if (machineStepFailed && registry.ChangesState(call.Name))
                            outcome = HandsResult.Fail("not_executed", "前面的机器步骤未成功，已跳过本批后续操作。请先核对机器状态。");
                        else
                        {
                            if (call.Name == MachineService.WaitTool) progress?.Invoke(T("正在等待机器…"));
                            if (call.Name == MacroService.RunTool) progress?.Invoke(T("正在本地运行宏…"));
                            try { outcome = await registry.ExecuteAsync(call.Name, ParseArguments(call.Arguments), cancellationToken); }
                            catch (JsonException) { outcome = HandsResult.Fail("invalid_arguments", T("工具参数不是有效的 JSON 对象，请修正参数。")); }
                        }
                        if (!outcome.Success && (call.Name == MachineService.WaitTool || call.Name == MachineService.OperateTool))
                            machineStepFailed = true;
                        if (!outcome.Success && call.Name == MacroService.RunTool) macroFailed = true;
                        exchange.Add(ToolReply(call.Id, outcome));
                        outstanding.Remove(call);
                        if (registry.ChangesState(call.Name)) result.Operations.Add(outcome.Message);
                        progress?.Invoke(call.Name + (IsEnglish ? ":" : "：") + outcome.Message);
                        await Task.Yield();
                    }
                }
            }
            catch (Exception error)
            {
                foreach (var call in outstanding)
                    exchange.Add(ToolReply(call.Id, HandsResult.Fail("not_executed", T("本轮在此操作执行前停止，没有执行这个调用。"))));
                var reason = error is OperationCanceledException && cancellationToken.IsCancellationRequested
                    ? T("本轮已停止。")
                    : error is ApiRequestException ? error.Message : T("本轮未完成。");
                exchange.Add(new JObject { ["role"] = "assistant", ["content"] =
                    reason + "\n" + T("已完成的工具操作保留，请根据本轮工具结果重新查询实际状态后继续。") });
                memory?.Commit(exchange);
                throw;
            }
            finally { IsRunning = false; }
        }

        private JObject ReadShopContext(HandsRunResult result, CancellationToken cancellationToken)
        {
            var observations = new JArray();
            void Read(string name, JObject arguments)
            {
                if (!registry.Contains(name) || registry.ChangesState(name)) return;
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = registry.Execute(name, arguments);
                observations.Add(new JObject { ["tool"] = name, ["arguments"] = arguments.DeepClone(),
                    ["result"] = JObject.Parse(HandsProtocol.Json(outcome)) });
            }
            // Only these built-in reads are automatic. Never run a mutation or a future tool here.
            Read("get_current_customer", new JObject());
            Read("list_container", new JObject { ["container_id"] = "counter", ["limit"] = 100 });
            if (observations.Count == 0) return null;
            result.ToolCalls += observations.Count;
            // Ephemeral input data uses the common message format, with no invented model reasoning
            // or tool calls. It is neither shown as user chat nor committed to conversation history.
            return new JObject { ["role"] = "user", ["content"] = new JObject
            {
                ["kind"] = "shop_observation", ["observation_scope"] = "request_start",
                ["context_note"] = T("这是本条消息开始时重新读取的实况，以成功返回的数据为准。刷新失败时当前状态未知，不能用历史记录补齐。柜台物品的 owned=false 表示已非主人所有，不要当作待售自有物品。"),
                ["observations"] = observations
            }.ToString(Formatting.None) };
        }

        private static JObject ToolReply(string id, HandsResult outcome)
            => new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = HandsProtocol.Json(outcome) };

        private static JObject ParseArguments(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None };
            var arguments = JObject.Load(reader);
            if (reader.Read()) throw new JsonReaderException();
            return arguments;
        }
    }
}
