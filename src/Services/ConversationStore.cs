using System;

namespace ProbablyAI.Services
{
    // Inject the persistence callbacks so tests can use independent save snapshots.
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
        public ConversationStore(Func<string> read, Action<string> write, Action delete)
        {
            this.read = read;
            this.write = write;
            this.delete = delete;
        }

        public string Load() => read == null ? null : read();
        public void Save(string text) => write?.Invoke(text);
        public void Delete() => delete?.Invoke();
    }
}
