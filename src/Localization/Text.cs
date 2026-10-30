using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ProbablyAI.Localization
{
    public static class Text
    {
        private static volatile bool english;
        public static bool IsEnglish => english;

        // The game's locale, rather than the Windows or Steam UI language, is authoritative.
        // Cache it on Unity's main thread; HTTP error handling may read strings on a worker.
        public static bool UseLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return false;
            bool next = !locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            bool changed = english != next;
            english = next;
            return changed;
        }

        public static string T(string chinese)
            => english && chinese != null && EnglishTranslations.Values.TryGetValue(chinese, out var translated) ? translated : chinese;

        public static string F(string chinese, params object[] args)
            => string.Format(CultureInfo.InvariantCulture, T(chinese), args);

        public static void TranslateToolDescriptions(JToken token)
        {
            if (token is JProperty property && property.Name == "description" && property.Value.Type == JTokenType.String)
                property.Value = T((string)property.Value);
            else if (token is JContainer container)
                foreach (var child in container.Children()) TranslateToolDescriptions(child);
        }
    }
}
