using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            using (var sha = SHA256.Create())
                hash = System.Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized.BaseUrl + "\n" + normalized.Model + "\n" + normalized.ApiKey)));
            bool changed = connectionHash != null && connectionHash != hash;
            if (changed) Clear();
            connectionHash = hash;
            return changed;
        }

        public JArray Messages() => new JArray(turns.SelectMany(t => t).Select(t => t.DeepClone()));
        public void Commit(JArray exchange)
        {
            turns.Enqueue((JArray)exchange.DeepClone());
            while (turns.Count > maxTurns || turns.Sum(t => t.ToString(Formatting.None).Length) > maxCharacters)
                turns.Dequeue();
        }
        public void Clear() => turns.Clear();
    }
}
