// Native surfaces used to exercise the production inspection adapter without Unity or a save.
using System;
using System.Collections.Generic;
using ProbablyAI.Hands;

namespace ProbablyAI.Hands
{
    public sealed class GameHandsBackend
    {
        public WorldSnapshot World = new WorldSnapshot();
        internal Dictionary<string, GameItem> Items = new Dictionary<string, GameItem>();
        public int Captures;
        public Exception Failure;
        public Action Refresh;
        public WorldSnapshot Capture()
        {
            Captures++;
            if (Failure != null) throw Failure;
            Refresh?.Invoke();
            return World;
        }
        internal GameItem ResolveCurrentItem(string id)
            => Items.TryGetValue(id, out var item) ? item : throw new HandsException("item_not_found", "物品已不在当前容器树中。");
    }
}

internal static class AppHandHelper
{
    public static bool Tactical, Deluxe;
    public static int GetCorrectValue(GameItem item) { item.HandValueReads++; return item.NativeHandValue; }
    public static bool IsTacticalSet(string material, string control, string condition, string armored, string bladed) => Tactical;
    public static bool IsDeluxeSet(string material, string control, string condition) => Deluxe;
}

internal static class InsCigaretteHelper
{
    public const int SEAL_COUNT = 2, LOGO_COUNT = 5, LETTERING_COUNT = 8, COLOR_COUNT = 4;
}

internal static class InsStampHelper
{
    public const int STAMP_SEAL_COUNT = 2, STAMP_LOGO_COUNT = 6, STAMP_CHIP_COUNT = 4, STAMP_LETTERING_COUNT = 9;
}

internal static class InsInjectorHelper
{
    public const string CORRECT_COMPANY = "Carmicheal Pharmaceutical";
    public const string CORRECT_MODEL = "Immunivax";
    public static readonly int[] RECALLED_PREFIX = { 250, 251, 301, 355, 385, 386, 421 };
}

internal static class RibwichHelper
{
    public const string RIBWICH_TAG = "RIBWICH_TAG";
    public const string RIBWICH_WRAP_TAG = "RIBWICH_WRAP_TAG";
    public const string RIBWICH_PICKLES_TAG = "RIBWICH_PICKLES_TAG";
    public const string RIBWICH_MEAT_TAG = "RIBWICH_MEAT_TAG";
    public const string RIBWICH_BUN_TAG = "RIBWICH_BUN_TAG";
    public const string RIBWICH_DAVRE_LOGO_TAG = "RIBWICH_DAVRE_LOGO_TAG";
    public const string RIBWICH_RIBWICH_LOGO_TAG = "RIBWICH_RIBWICH_LOGO_TAG";
    public const string RIBWICH_PRODUCTION_MONTH_TAG = "RIBWICH_PRODUCTION_MONTH_TAG";
    public const string RIBWICH_PRODUCTION_YEAR_TAG = "RIBWICH_PRODUCTION_YEAR_TAG";
    public const string RIBWICH_FACILITY_NUMBER_TAG = "RIBWICH_FACILITY_NUMBER_TAG";
    public const string RIBWICH_PRODUCTION_TAG = "RIBWICH_PRODUCTION_TAG";
    public const string RIBWICH_FACILITY_TAG = "RIBWICH_FACILITY_TAG";
    public const int RIBWICH_WRAP_CORRECT = 0, RIBWICH_WRAP_OPEN_RIGHT = 2;
    public const int RIBWICH_MEAT_DOUBLE = 1;
    public const int RIBWICH_BUN_DELUXE = 1, RIBWICH_PICKLES_EXTRA = 1;
    public const int INCORRECT_DAVRE_LOGO_COUNT = 7, INCORRECT_RIBWICH_LOGO_COUNT = 3;
    public const int FIRST_PRODUCTION_DATE_YEAR_MIN = 425, FIRST_PRODUCTION_DATE_YEAR_MAX = 429;
    public const int SECOND_PRODUCTION_DATE_YEAR_MIN = 432, SECOND_PRODUCTION_DATE_YEAR_MAX = 438;
    public const int TOTAL_FACILITY_NUMBERS = 35;
    public static int GetCorrectValue(GameItem item) { item.RibwichValueReads++; return item.NativeRibwichValue; }
}

internal static class PlasmaHelper
{
    public const string PLASMA_CELL_TAG = "PLASMA_CELL_TAG";
    public const string PLASMA_CASING_CONDITION_CRACK = "PLASMA_CASING_CONDITION_CRACK";
    public const string PLASMA_CASING_CONDITION_UNCOVERED = "PLASMA_CASING_CONDITION_UNCOVERED";
    public const string PLASMA_CASING_CONDITION_REINFORCED = "PLASMA_CASING_CONDITION_REINFORCED";
    public const string PLASMA_CASING_CONDITION_WEAR = "PLASMA_CASING_CONDITION_WEAR";
    public const string PLASMA_COLOR = "PLASMA_COLOR";
    public const string PLASMA_SEAL = "PLASMA_SEAL";
    public const string NONE = "NONE";
    public const string CRACK = "CRACK";
    public const string LL = "LL";
    public const string ZHONGHUA = "ZHONGHUA";
    public const string REPUBLIC = "REPUBLIC";
    public const string IGNITION_LAB = "IGNITION_LAB";
    public const string STANDARD = "STANDARD";
    public const string HIGH_QUALITY = "HIGH_QUALITY";
    public const string POOR1 = "POOR1";
    public const string POOR2 = "POOR2";
    public const string WEAR = "WEAR";
    public const string WEAR_HEAVY = "WEAR_HEAVY";
    public const string UNCOVERED = "UNCOVERED";
    public const string REINFORCED = "REINFORCED";
    public static int GetCorrectValue(GameItem item) { item.PlasmaValueReads++; return item.NativePlasmaValue; }
}
