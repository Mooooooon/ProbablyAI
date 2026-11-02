using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProbablyAI.Services
{
    // A deterministic, provider-independent estimate for the history budget. It is not a
    // tokenizer: mixed Chinese text is roughly one token per character, while JSON/ASCII text
    // is roughly three characters per token. The limit is deliberately conservative.
    public static class TokenEstimator
    {
        public const int MessageOverhead = 4;

        public static int EstimateText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Math.Max(1, (Encoding.UTF8.GetByteCount(text) + 2) / 3);
        }

        public static int Estimate(JToken token)
        {
            if (token == null) return 0;
            string text = token.Type == JTokenType.String
                ? (string)token
                : token.ToString(Formatting.None);
            return EstimateText(text) + MessageOverhead;
        }

        public static int EstimateMany(IEnumerable<JToken> tokens)
        {
            int total = 0;
            if (tokens == null) return total;
            foreach (var token in tokens) total += Estimate(token);
            return total;
        }
    }
}
