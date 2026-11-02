using System;

namespace ProbablyAI.Services
{
    // The conversation file is injected so tests never touch the player's ModData folder.
    public interface IConversationStore
    {
        string Load();
        void Save(string text);
        void Delete();
    }

    public sealed class ConversationStore : IConversationStore
    {
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly Action delete;
        public string FileName { get; }

        public ConversationStore(string fileName, Func<string> read, Action<string> write, Action delete)
        {
            FileName = fileName;
            this.read = read;
            this.write = write;
            this.delete = delete;
        }

        public string Load() => read == null ? null : read();
        public void Save(string text) => write?.Invoke(text);
        public void Delete() => delete?.Invoke();
    }
}
