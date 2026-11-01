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
        InjectorInspection InspectInjector(string itemId);
        WaterInspection AnalyzeWater(string itemId);
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

    public sealed class InjectorInspection
    {
        public ItemInfo Item { get; set; }
        public string VisualAssessment { get; set; }
        // true only for an apparently intact, non-recalled, non-expired injector; null when uncertain.
        public bool? Usable { get; set; }
        public int Indicator { get; set; }
        public int Body { get; set; }
        public int Color { get; set; }
        public string Serial { get; set; }
        public string Company { get; set; }
        public string Model { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    public sealed class WaterInspection
    {
        public ItemInfo Item { get; set; }
        public bool Empty { get; set; }
        public int VolumeMl { get; set; }
        public int CapacityMl { get; set; }
        public double FillPercent { get; set; }
        public string LabelGrade { get; set; }
        public string LabelPurityRange { get; set; }
        public bool LabelHazardous { get; set; }
        // tested means purity is already visible through a strip, exposure or an aquascan.
        public bool Tested { get; set; }
        public int? Purity { get; set; }
        public string PurityPercent { get; set; }
        public string Grade { get; set; }
        public string GradeRange { get; set; }
        public bool? LabelMatchesTest { get; set; }
        public string Potable { get; set; }
        public string AssessmentSource { get; set; }
        public bool ContentsRevealed { get; set; }
        public List<WaterContent> Contents { get; set; }
        public string LastScan { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    public sealed class WaterContent
    {
        public string Liquid { get; set; }
        public string DisplayName { get; set; }
        public int AmountUl { get; set; }
        public int AmountMl { get; set; }
        public double Percent { get; set; }
    }
}
