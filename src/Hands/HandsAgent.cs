using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Services;
using ProbablyAI.Tools;

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
        public bool IsRunning { get; private set; }

        public HandsAgent(IChatTransport transport, HandsService hands) : this(transport, ToolRegistry.ForHands(hands)) { }
        public HandsAgent(IChatTransport transport, ToolRegistry registry) { this.transport = transport; this.registry = registry; }

        public async Task<HandsRunResult> RunAsync(ConnectionOptions options, string instruction, Action<string> progress, CancellationToken cancellationToken, ConversationMemory memory = null)
        {
            if (IsRunning) throw new InvalidOperationException("助手正在处理上一条消息，请等待完成或先停止。");
            if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 6000)
                throw new ArgumentException("请输入 1–6000 字的消息。");
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
                if (memory != null) foreach (var message in memory.Messages()) prefix.Add(message.DeepClone());
                for (int round = 0; round < 16; round++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke("正在思考…");
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
                        throw new ApiRequestException("工具调用达到上限或出现重复调用 ID，已停止。请查看操作记录后继续。");
                    foreach (var call in turn.ToolCalls)
                        if (!callIds.Add(call.Id)) throw new ApiRequestException("模型重复了调用 ID，已停止。请检查操作记录。");
                    exchange.Add(turn.Message.DeepClone());
                    outstanding.AddRange(turn.ToolCalls);
                    foreach (var call in turn.ToolCalls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.ToolCalls++;
                        HandsResult outcome;
                        try { outcome = registry.Execute(call.Name, JObject.Parse(call.Arguments)); }
                        catch (JsonException) { outcome = HandsResult.Fail("invalid_arguments", "工具参数不是有效的 JSON 对象，请修正参数。"); }
                        exchange.Add(ToolReply(call.Id, outcome));
                        outstanding.Remove(call);
                        if (registry.ChangesState(call.Name)) result.Operations.Add(outcome.Message);
                        progress?.Invoke(call.Name + "：" + outcome.Message);
                        await Task.Yield();
                    }
                }
                result.Reply = "已达到本次消息的处理轮数上限。请查看操作记录，必要时继续。";
                exchange.Add(new JObject { ["role"] = "assistant", ["content"] = result.Reply });
                memory?.Commit(exchange);
                return result;
            }
            catch
            {
                foreach (var call in outstanding)
                    exchange.Add(ToolReply(call.Id, HandsResult.Fail("not_executed", "本轮在此操作执行前停止，没有执行这个调用。")));
                exchange.Add(new JObject { ["role"] = "assistant", ["content"] = "本轮未完成或已停止。已完成的工具操作保留，请根据本轮工具结果重新查询实际状态后继续。" });
                memory?.Commit(exchange);
                throw;
            }
            finally { IsRunning = false; }
        }

        private static JObject ToolReply(string id, HandsResult outcome)
            => new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = HandsProtocol.Json(outcome) };
    }
}
