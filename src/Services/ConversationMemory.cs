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
        public JArray MessagesForModel(Func<string, bool> changesState)
        {
            var messages = Messages();
            var names = new Dictionary<string, string>();
            foreach (JObject message in messages)
            {
                if (message["tool_calls"] is JArray calls)
                    foreach (var call in calls)
                        names[(string)call["id"]] = (string)call["function"]?["name"];
                if ((string)message["role"] != "tool") continue;
                names.TryGetValue((string)message["tool_call_id"], out var name);
                JObject result;
                if (name != null && changesState(name))
                {
                    try { result = JObject.Parse((string)message["content"]); }
                    catch (JsonException) { result = new JObject(); }
                }
                else result = new JObject { ["code"] = "historical_read_omitted" };
                result["historical"] = true;
                result["context_note"] = T("这是历史记录，不代表当前状态。旧查询数据已过期；当前物品、位置、所有权、客人、价格和预算须重新查询。已完成的操作仅作为过去发生的事实保留，不要重复执行。");
                message["content"] = result.ToString(Formatting.None);
            }
            return messages;
        }
        public void Commit(JArray exchange)
        {
            turns.Enqueue((JArray)exchange.DeepClone());
            while (turns.Count > maxTurns || turns.Sum(t => t.ToString(Formatting.None).Length) > maxCharacters)
                turns.Dequeue();
        }
        public void Clear() => turns.Clear();
    }
}
