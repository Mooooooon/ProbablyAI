using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            IsRunning = true;
            try
            {
                memory?.BindConnection(connection);
                var prefix = new JArray { new JObject { ["role"] = "system", ["content"] = connection.SystemPrompt } };
                if (memory != null) foreach (var message in memory.MessagesForModel(registry.ChangesState)) prefix.Add(message.DeepClone());
                if (refreshShopContext)
                {
                    var observation = ReadShopContext(result, cancellationToken);
                    if (observation != null) prefix.Add(observation);
                }
                for (int round = 0; round < 16; round++)
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
                    if (result.ToolCalls + turn.ToolCalls.Count > 48)
                        throw new ApiRequestException(T("工具调用达到上限或出现重复调用 ID，已停止。请查看操作记录后继续。"));
                    foreach (var call in turn.ToolCalls)
                        if (!callIds.Add(call.Id)) throw new ApiRequestException(T("模型重复了调用 ID，已停止。请检查操作记录。"));
                    exchange.Add(turn.Message.DeepClone());
                    outstanding.AddRange(turn.ToolCalls);
                    foreach (var call in turn.ToolCalls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.ToolCalls++;
                        HandsResult outcome;
                        try { outcome = registry.Execute(call.Name, JObject.Parse(call.Arguments)); }
                        catch (JsonException) { outcome = HandsResult.Fail("invalid_arguments", T("工具参数不是有效的 JSON 对象，请修正参数。")); }
                        exchange.Add(ToolReply(call.Id, outcome));
                        outstanding.Remove(call);
                        if (registry.ChangesState(call.Name)) result.Operations.Add(outcome.Message);
                        progress?.Invoke(call.Name + (IsEnglish ? ":" : "：") + outcome.Message);
                        await Task.Yield();
                    }
                }
                result.Reply = T("已达到本次消息的处理轮数上限。请查看操作记录，必要时继续。");
                exchange.Add(new JObject { ["role"] = "assistant", ["content"] = result.Reply });
                memory?.Commit(exchange);
                return result;
            }
            catch
            {
                foreach (var call in outstanding)
                    exchange.Add(ToolReply(call.Id, HandsResult.Fail("not_executed", T("本轮在此操作执行前停止，没有执行这个调用。"))));
                exchange.Add(new JObject { ["role"] = "assistant", ["content"] = T("本轮未完成或已停止。已完成的工具操作保留，请根据本轮工具结果重新查询实际状态后继续。") });
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
    }
}
