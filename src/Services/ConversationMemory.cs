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
    // Keeps the complete visible transcript in memory and on disk. The turn/token limits only
    // select which complete turns are sent to the model; they never delete stored history.
    // Only an explicit Clear (or a connection/persona change) removes the archive.
    public sealed class ConversationMemory
    {
        private const int FormatVersion = 1;
        private const int MaxStoredCharacters = 16 * 1024 * 1024;
        private readonly Queue<JArray> turns = new Queue<JArray>();
        private readonly IConversationStore store;
        private int maxTurns;
        private int maxTokens;
        private int noticeLifetimeTurns;
        private string connectionHash;
        private string loadedConnectionHash;
        private string lastSaved;
        public int TurnCount => turns.Count;
        public int MaxTurns => maxTurns;
        public int MaxTokens => maxTokens;
        public string LoadWarning { get; private set; }
        public string SaveWarning { get; private set; }

        public ConversationMemory(int maxTurns = ConnectionOptions.DefaultHistoryTurns,
            int maxTokens = ConnectionOptions.DefaultHistoryTokens, IConversationStore store = null)
        {
            this.store = store;
            Configure(maxTurns, maxTokens);
            Restore();
        }

        // Limits are per request and measured on the projected model transcript. They do not
        // remove turns from the visible or stored conversation.
        public void Configure(int turnLimit, int tokenLimit)
        {
            if (turnLimit < 1 || tokenLimit < 1) throw new ArgumentOutOfRangeException();
            maxTurns = turnLimit;
            maxTokens = tokenLimit;
            noticeLifetimeTurns = turnLimit;
        }

        public bool BindConnection(ConnectionOptions options)
        {
            var normalized = options.Normalized();
            string hash = Identity(normalized);
            bool changed = connectionHash != null && connectionHash != hash;
            if (!changed && connectionHash == null && !string.IsNullOrEmpty(loadedConnectionHash) && loadedConnectionHash != hash)
            {
                // The stored conversation belongs to another model/persona; never expose it here.
                Clear();
                changed = true;
            }
            if (changed && connectionHash != null) Clear();
            loadedConnectionHash = null;
            connectionHash = hash;
            return changed;
        }

        private static string Identity(ConnectionOptions options)
        {
            var identity = JsonConvert.SerializeObject(new[] { options.BaseUrl, options.Model, options.ApiKey, options.SystemPrompt });
            using (var sha = SHA256.Create())
                return System.Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
        }

        // All visible turns, including local arrival notices.
        public JArray Messages() => new JArray(turns.SelectMany(t => t).Select(t => t.DeepClone()));

        // Project the complete archive for callers that still need the full model transcript.
        public JArray MessagesForModel(Func<string, bool> changesState) => Project(turns, changesState);

        // Select the newest complete turns that fit the current send budget. The archive is not
        // changed, so raising the limits later can reuse turns that were not sent in this request.
        public JArray ContextMessages(int turnLimit, int tokenLimit, Func<string, bool> changesState)
        {
            if (turnLimit < 1 || tokenLimit < 1) throw new ArgumentOutOfRangeException();
            noticeLifetimeTurns = turnLimit;
            var all = turns.ToArray();
            var selected = new List<JArray>();
            for (int i = all.Length - 1; i >= 0 && selected.Count < turnLimit; i--)
            {
                if (!HasModelMessages(all[i])) continue;
                selected.Insert(0, all[i]);
                if (TokenEstimator.EstimateMany(Project(selected, changesState)) > tokenLimit)
                {
                    selected.RemoveAt(0);
                    break;
                }
            }
            return Project(selected, changesState);
        }

        public int EstimateForModel(Func<string, bool> changesState)
            => TokenEstimator.EstimateMany(MessagesForModel(changesState));

        public void Commit(JArray exchange)
        {
            turns.Enqueue((JArray)exchange.DeepClone());
            ExpireNotices();
            Persist();
        }

        private void ExpireNotices()
        {
            // Arrival notices are local UI hints, not conversation. They expire after the
            // configured number of completed turns even though normal turns are retained.
            if (noticeLifetimeTurns < 1) return;
            var keep = new List<JArray>();
            foreach (var turn in turns)
            {
                if (!IsLocalOnly(turn)) { keep.Add(turn); continue; }
                int age = 0;
                foreach (JObject message in turn)
                    age = Math.Max(age, (int?)message["notice_age"] ?? 0);
                age++;
                if (age >= noticeLifetimeTurns) continue;
                foreach (JObject message in turn) message["notice_age"] = age;
                keep.Add(turn);
            }
            turns.Clear();
            foreach (var turn in keep) turns.Enqueue(turn);
        }

        private static bool IsLocalOnly(JArray turn)
            => turn.All(message => (bool?)message?["local"] == true);

        private static bool HasModelMessages(JArray turn)
            => turn.Any(message => (bool?)message?["local"] != true && IsModelRole((string)message?["role"]));

        private static bool IsModelRole(string role)
            => role == "user" || role == "assistant" || role == "tool";

        private static JArray Project(IEnumerable<JArray> source, Func<string, bool> changesState)
        {
            var result = new JArray();
            var names = new Dictionary<string, string>();
            foreach (JArray turn in source)
            {
                foreach (JObject message in turn)
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
            }
            return result;
        }

        private void Restore()
        {
            if (store == null) return;
            string text;
            try { text = store.Load(); }
            catch (Exception) { LoadWarning = T("聊天记录读取失败，已从新对话开始。"); return; }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > MaxStoredCharacters)
            {
                LoadWarning = T("聊天记录文件过大，已忽略；可以手动清空聊天后继续。");
                return;
            }
            try
            {
                var root = JObject.Parse(text);
                if ((int?)root["version"] != FormatVersion) return;
                loadedConnectionHash = (string)root["connection"];
                if (!(root["turns"] is JArray storedTurns)) return;
                foreach (var token in storedTurns)
                {
                    if (!(token is JArray turn)) continue;
                    var clean = new JArray();
                    foreach (var messageToken in turn)
                    {
                        if (!(messageToken is JObject message)) continue;
                        if (!IsModelRole((string)message["role"])) continue;
                        if ((bool?)message["local"] == true) continue;
                        clean.Add(message.DeepClone());
                    }
                    if (clean.Count > 0) turns.Enqueue(clean);
                }
            }
            catch (Exception)
            {
                turns.Clear();
                LoadWarning = T("聊天记录格式无法识别，已从新对话开始。");
            }
        }

        private void Persist()
        {
            if (store == null) return;
            var storedTurns = new JArray();
            foreach (var turn in turns)
            {
                var clean = new JArray();
                foreach (JObject message in turn)
                    if ((bool?)message["local"] != true) clean.Add(message.DeepClone());
                if (clean.Count > 0) storedTurns.Add(clean);
            }
            if (storedTurns.Count == 0)
            {
                DeleteStore();
                lastSaved = null;
                return;
            }
            var root = new JObject
            {
                ["version"] = FormatVersion,
                ["connection"] = connectionHash ?? loadedConnectionHash ?? "",
                ["turns"] = storedTurns
            };
            string text = root.ToString(Formatting.None);
            if (text.Length > MaxStoredCharacters)
            {
                SaveWarning = T("聊天记录过大，未写入本机；本次对话仍可继续。请手动清空聊天记录。");
                return;
            }
            if (text == lastSaved) return;
            try
            {
                store.Save(text);
                lastSaved = text;
                SaveWarning = null;
            }
            catch (Exception)
            {
                SaveWarning = T("聊天记录保存失败；本次对话仍可继续，但重启后可能丢失。");
            }
        }

        private void DeleteStore()
        {
            try { store?.Delete(); }
            catch (Exception) { SaveWarning = T("聊天记录删除失败；下次启动可能重新载入旧对话。"); }
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
                ["local"] = true,
                ["notice_age"] = 0
            };
            turns.Enqueue(new JArray { message });
            Persist();
        }

        public void Clear()
        {
            turns.Clear();
            loadedConnectionHash = null;
            lastSaved = null;
            DeleteStore();
        }
    }
}
