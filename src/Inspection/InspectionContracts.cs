using System.Collections.Generic;
using ProbablyAI.Hands;

namespace ProbablyAI.Inspection
{
    public interface IInspectionBackend
    {
        MechanicalHandInspection AppraiseMechanicalHand(string itemId);
        RibwichInspection AppraiseRibwich(string itemId);
        PlasmaFuelInspection AppraisePlasmaFuel(string itemId);
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

    public sealed class RibwichInspection
    {
        public ItemInfo Item { get; set; }
        public int Meat { get; set; }
        public int Bun { get; set; }
        public int Pickles { get; set; }
        public int Wrap { get; set; }
        public bool Opened { get; set; }
        public bool DoubleMeat { get; set; }
        public bool DeluxeEdition { get; set; }
        public int DavreLogo { get; set; }
        public int RibwichLogo { get; set; }
        public int FacilityNumber { get; set; }
        public int ProductionMonth { get; set; }
        public int ProductionYear { get; set; }
        public bool BarcodePrinted { get; set; }
        public bool FacilityPrinted { get; set; }
        public string VisualAssessment { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public int BaseValue { get; set; }
        public string Note { get; set; }
    }

    public sealed class PlasmaFuelInspection
    {
        public ItemInfo Item { get; set; }
        public string Color { get; set; }
        public string Crack { get; set; }
        public string Wear { get; set; }
        public string Uncovered { get; set; }
        public string Reinforced { get; set; }
        public string Seal { get; set; }
        public string ConditionAssessment { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
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
