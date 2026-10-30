using System;
using Newtonsoft.Json;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Services
{
    // Persistence is injected so tests never access a real key or the game's save folder.
    public sealed class ConnectionSettingsStore
    {
        private readonly Action<string> write;
        private ConnectionOptions current = new ConnectionOptions();
        private bool loadFailed;
        public string LoadWarning => loadFailed ? T("配置无法读取，已使用默认连接和提示词。请重新填写并保存。") : null;
        public ConnectionOptions Current => current.Normalized(); // Built-in prompts follow the current game language; custom text is preserved.

        public ConnectionSettingsStore(Func<string> read, Action<string> write)
        {
            this.write = write;
            try
            {
                var text = read();
                if (string.IsNullOrWhiteSpace(text)) return;
                var loaded = JsonConvert.DeserializeObject<ConnectionOptions>(text);
                if (loaded == null) throw new ArgumentException();
                if (!loaded.RememberApiKey) loaded.ApiKey = "";
                current = loaded.Normalized();
            }
            catch (Exception)
            {
                loadFailed = true;
            }
        }

        public void Save(ConnectionOptions options)
        {
            var normalized = options.Normalized();
            var stored = normalized.Copy();
            if (!stored.RememberApiKey) stored.ApiKey = "";
            try
            {
                write(JsonConvert.SerializeObject(stored, Formatting.Indented));
            }
            catch (Exception)
            {
                throw new InvalidOperationException(T("配置保存失败，请检查本机配置目录是否可写。"));
            }
            current = normalized;
            loadFailed = false;
        }
    }
}
