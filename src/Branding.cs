namespace ProbablyAI
{
    public static class Branding
    {
        public const string Name = "深空AI：可能是DeepSeek";
        public const string ShortName = "深空AI";
        public static string DisplayName => Localization.Text.T(Name);
        public static string DisplayShortName => Localization.Text.T(ShortName);
        public const string Version = "0.6.1";
    }
}
