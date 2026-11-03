using System.Collections.Generic;
using ProbablyAI.Hands;

namespace ProbablyAI.Labels
{
    public interface ILabelBackend
    {
        LabelOptions Inspect(string toolItemId, string targetItemId);
        LabelChange SetLabel(string toolItemId, string targetItemId, string categoryId, string conditionId);
        LabelChange Rename(string toolItemId, string targetItemId, string name);
    }

    public sealed class LabelOptions
    {
        public ItemInfo Item { get; set; }
        public string ToolItemId { get; set; }
        public string Mode { get; set; }
        public string CustomName { get; set; }
        public int? NameCharacterLimit { get; set; }
        public List<EditableLabel> Labels { get; set; } = new List<EditableLabel>();
    }

    public sealed class EditableLabel
    {
        public string CategoryId { get; set; }
        public string Name { get; set; }
        public string CurrentConditionId { get; set; }
        public string CurrentDisplay { get; set; }
        public bool Exposed { get; set; }
        public List<LabelChoice> Choices { get; set; } = new List<LabelChoice>();
    }

    public sealed class LabelChoice
    {
        public string ConditionId { get; set; }
        public string Display { get; set; }
    }

    public sealed class LabelChange
    {
        public LabelOptions Before { get; set; }
        public LabelOptions After { get; set; }
        public bool Changed { get; set; }
    }
}
