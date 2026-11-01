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
        private readonly int maxTurns;
        private readonly int maxCharacters;
        private string connectionHash;
        public int TurnCount => turns.Count;

        public ConversationMemory(int maxTurns = 8, int maxCharacters = 100000)
        {
            if (maxTurns < 1 || maxCharacters < 1) throw new System.ArgumentOutOfRangeException();
            this.maxTurns = maxTurns; this.maxCharacters = maxCharacters;
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
                    if (name != null && changesState(name))
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

        public void Commit(JArray exchange)
        {
            turns.Enqueue((JArray)exchange.DeepClone());
            TrimTurns();
        }

        private void TrimTurns()
        {
            while (turns.Count > maxTurns || turns.Sum(t => t.ToString(Formatting.None).Length) > maxCharacters)
                turns.Dequeue();
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
