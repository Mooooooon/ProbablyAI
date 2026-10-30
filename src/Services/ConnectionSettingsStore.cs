using System;
using Newtonsoft.Json;

namespace ProbablyAI.Services
{
    // Persistence is injected so tests never access a real key or the game's save folder.
    public sealed class ConnectionSettingsStore
    {
        private readonly Action<string> write;
        private ConnectionOptions current = new ConnectionOptions();
        public string LoadWarning { get; private set; }
        public ConnectionOptions Current => current.Copy();

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
                LoadWarning = "连接配置无法读取，已使用默认地址和模型。请重新填写并保存。";
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
                throw new InvalidOperationException("配置保存失败，请检查本机配置目录是否可写。");
            }
            current = normalized;
            LoadWarning = null;
        }
    }
}
