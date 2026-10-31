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
