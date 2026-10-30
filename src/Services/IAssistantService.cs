using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    // Provider implementations must keep Unity access on the main thread.
    // Capture context before awaiting and accept cancellation on scene changes.
    public interface IAssistantService
    {
        Task<IReadOnlyList<string>> ListModelsAsync(ConnectionOptions options, CancellationToken cancellationToken);
        Task<string> ReplyAsync(ConnectionOptions options, string message, AssistantContext context, CancellationToken cancellationToken);
    }

    public sealed class AssistantContext
    {
        public int Day { get; set; }
        public int Cash { get; set; }
        public bool InStore { get; set; }

        public static AssistantContext Capture()
        {
            return new AssistantContext
            {
                Day = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : 0,
                Cash = PlayerStore.Instance.playerCash,
                InStore = PlayerStore.Instance.playerLocation == PlayerStore.PlayerLocation.IN_STORE
            };
        }
    }

    public sealed class OpenAiAssistantService : IAssistantService
    {
        private readonly OpenAiApiClient client;

        public OpenAiAssistantService(OpenAiApiClient client) => this.client = client;

        public Task<IReadOnlyList<string>> ListModelsAsync(ConnectionOptions options, CancellationToken cancellationToken)
            => client.ListModelsAsync(options, cancellationToken);

        public Task<string> ReplyAsync(ConnectionOptions options, string message, AssistantContext context, CancellationToken cancellationToken)
        {
            var connection = options.Normalized();
            var prompt = connection.SystemPrompt;
            if (context != null)
                prompt += F(" 当前是第 {0} 天，持有 {1} 信用点，当铺内：{2}。", context.Day, context.Cash, context.InStore);
            return client.CompleteAsync(connection, message, prompt, cancellationToken);
        }
    }
}
