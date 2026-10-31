using System.Collections.Generic;
using ProbablyAI.Hands;

namespace ProbablyAI.Documents
{
    public interface IDocumentBackend
    {
        DocumentContent Read(string itemId);
    }

    public sealed class DocumentContent
    {
        public ItemInfo Item { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Signature { get; set; }
        public List<DocumentPage> Pages { get; set; } = new List<DocumentPage>();
        public string Text { get; set; }
        public string Note { get; set; }
    }

    public sealed class DocumentPage
    {
        public int Number { get; set; }
        public string Subtitle { get; set; }
        public string Body { get; set; }
    }
}
