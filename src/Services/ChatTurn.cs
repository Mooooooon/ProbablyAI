using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ProbablyAI.Services
{
    public sealed class ChatTurn
    {
        // Preserve reasoning_content and tool_calls verbatim for compatible reasoning models.
        public JObject Message { get; set; }
        public string Text { get; set; }
        public IReadOnlyList<ChatToolCall> ToolCalls { get; set; } = new ChatToolCall[0];
    }

    public sealed class ChatToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Arguments { get; set; }
    }

    public interface IChatTransport
    {
        Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken cancellationToken);
    }
}
