using System.Collections.Generic;
using ProbablyAI.Hands;

namespace ProbablyAI.Inspection
{
    public interface IInspectionBackend
    {
        MechanicalHandInspection AppraiseMechanicalHand(string itemId);
        CigaretteInspection InspectCigarette(string itemId);
        FoodStampInspection InspectFoodStamp(string itemId);
    }

    public sealed class MechanicalHandInspection
    {
        public ItemInfo Item { get; set; }
        public string Material { get; set; }
        public string ControlInterface { get; set; }
        public string InterfaceMarking { get; set; }
        public string Condition { get; set; }
        public bool Armored { get; set; }
        public bool Bladed { get; set; }
        public bool TacticalSet { get; set; }
        public bool DeluxeSet { get; set; }
        public int BaseValue { get; set; }
        public string Note { get; set; }
    }

    public sealed class CigaretteInspection
    {
        public ItemInfo Item { get; set; }
        public int Seal { get; set; }
        public int Logo { get; set; }
        public int Lettering { get; set; }
        public int Box { get; set; }
        public string VisualAssessment { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    public sealed class FoodStampInspection
    {
        public ItemInfo Item { get; set; }
        public int Seal { get; set; }
        public int Logo { get; set; }
        public int Chip { get; set; }
        public int Lettering { get; set; }
        public int FaceValue { get; set; }
        public string VisualAssessment { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public string Note { get; set; }
    }
}
