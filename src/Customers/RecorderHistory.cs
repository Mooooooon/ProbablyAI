using System;
using System.Collections.Generic;

namespace ProbablyAI.Customers
{
    // The recorder keeps every displayed line in PlayerStore.dialogHistory, separated by "---".
    // Only the segment after the last separator belongs to the current visit.
    public static class RecorderHistory
    {
        private const string Separator = "---";

        public static List<DialogueLine> CurrentVisit(IEnumerable<string> lines)
        {
            var result = new List<DialogueLine>();
            if (lines == null) return result;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Trim() == Separator)
                {
                    result.Clear();
                    continue;
                }
                var parsed = Parse(line);
                if (parsed != null) result.Add(parsed);
            }
            return result;
        }

        private static DialogueLine Parse(string line)
        {
            var value = line.Trim();
            if (value.Length == 0 || value[0] == '<') return null; // Mod-source notes and rich-text-only lines.
            int colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return null;
            var speaker = value.Substring(0, colon).Trim();
            var text = value.Substring(colon + 1).Trim();
            return speaker.Length == 0 || text.Length == 0 ? null : new DialogueLine { Speaker = speaker, Text = text };
        }
    }
}
