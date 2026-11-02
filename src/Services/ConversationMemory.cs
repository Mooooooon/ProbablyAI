using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    // Keep whole exchanges so tool-call/result pairs are never cut in half.
    public sealed class ConversationMemory
    {
        private readonly Queue<JArray> turns = new Queue<JArray>();
        private int maxTurns;
        private int maxTokens;
        private string connectionHash;
        public int TurnCount => turns.Count;
        public int MaxTurns => maxTurns;
        public int MaxTokens => maxTokens;

        public ConversationMemory(int maxTurns = ConnectionOptions.DefaultHistoryTurns,
            int maxTokens = ConnectionOptions.DefaultHistoryTokens)
        {
            Configure(maxTurns, maxTokens);
        }

        // Apply the player's turn/token budget before a request. Limits are not part of the
        // connection identity: changing them keeps the existing conversation.
        public void TrimToContext(int turnLimit, int tokenLimit, Func<string, bool> changesState)
        {
            Configure(turnLimit, tokenLimit);
            while (turns.Count > maxTurns) turns.Dequeue();
            while (turns.Count > 0 && EstimateForModel(changesState) > maxTokens) turns.Dequeue();
        }

        public void Configure(int turnLimit, int tokenLimit)
        {
            if (turnLimit < 1 || tokenLimit < 1) throw new ArgumentOutOfRangeException();
            maxTurns = turnLimit;
            maxTokens = tokenLimit;
            while (turns.Count > maxTurns) turns.Dequeue();
        }

        public bool BindConnection(ConnectionOptions options)
        {
            var normalized = options.Normalized();
            string hash;
            var identity = JsonConvert.SerializeObject(new[] { normalized.BaseUrl, normalized.Model, normalized.ApiKey, normalized.SystemPrompt });
            using (var sha = SHA256.Create())
                hash = System.Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
            bool changed = connectionHash != null && connectionHash != hash;
            if (changed) Clear();
            connectionHash = hash;
            return changed;
        }

        public JArray Messages() => new JArray(turns.SelectMany(t => t).Select(t => t.DeepClone()));

        // Keep the visible transcript intact, but never resend old read snapshots as live evidence.
        // Local arrival notices stay out of the model transcript so the model cannot echo or invent them.
        public JArray MessagesForModel(Func<string, bool> changesState)
        {
            var result = new JArray();
            var names = new Dictionary<string, string>();
            foreach (JObject message in Messages())
            {
                if ((bool?)message["local"] == true) continue;
                var clone = (JObject)message.DeepClone();
                if (clone["tool_calls"] is JArray calls)
                    foreach (var call in calls)
                        names[(string)call["id"]] = (string)call["function"]?["name"];
                if ((string)clone["role"] == "tool")
                {
                    names.TryGetValue((string)clone["tool_call_id"], out var name);
                    JObject toolResult;
                    if (name != null && changesState != null && changesState(name))
                    {
                        try { toolResult = JObject.Parse((string)clone["content"]); }
                        catch (JsonException) { toolResult = new JObject(); }
                    }
                    else toolResult = new JObject { ["code"] = "historical_read_omitted" };
                    toolResult["historical"] = true;
                    toolResult["context_note"] = T("这是历史记录，不代表当前状态。旧查询数据已过期；当前物品、位置、所有权、客人、价格和预算须重新查询。已完成的操作仅作为过去发生的事实保留，不要重复执行。");
                    clone["content"] = toolResult.ToString(Formatting.None);
                }
                result.Add(clone);
            }
            return result;
        }

        // Estimated tokens of the transcript actually sent to the model, not the raw history.
        public int EstimateForModel(Func<string, bool> changesState)
            => TokenEstimator.EstimateMany(MessagesForModel(changesState));

        public void Commit(JArray exchange)
        {
            turns.Enqueue((JArray)exchange.DeepClone());
            TrimTurns();
        }

        private void TrimTurns()
        {
            while (turns.Count > maxTurns) turns.Dequeue();
        }

        // The local watcher posts these; they are shown in the chat window but never sent to the model.
        public IReadOnlyList<string> Notices() => turns
            .SelectMany(t => t)
            .Where(m => (bool?)m["local"] == true)
            .Select(m => (string)m["content"])
            .ToList();

        public void PostNotice(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var message = new JObject
            {
                ["role"] = "assistant",
                ["content"] = text.Trim(),
                ["local"] = true
            };
            turns.Enqueue(new JArray { message });
            TrimTurns();
        }

        public void Clear() => turns.Clear();
    }
}
