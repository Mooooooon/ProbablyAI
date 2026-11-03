using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    public static class ItemUseRules
    {
        public const string WaterTablet = "water_tablet";
        public const string NutrientTablet = "nutrient_tablet";
        public const string UVFilter = "uv_filter";
        public const string WaterRation = "water_ration";

        // The native target/activate gates decide ownership, capacity, compatibility and
        // consumption. The rules only keep the assistant inside non-combat, unlocked actions.
        public static ItemUseValidation ValidateUse(ItemUseReading source, ItemUseReading target)
        {
            if (source?.Item == null || target?.Item == null)
                return Error("item_not_found", T("物品已不在当前容器树中。"));
            if (!string.IsNullOrEmpty(source.Item.Id) && source.Item.Id == target.Item.Id)
                return Error("same_item", T("不能对物品自身使用。"));
            if (source.Combat)
                return Error("combat_item", T("武器、弹药、爆炸物、战斗能力或敌人目标不能由助手使用。"));
            if (target.Enemy)
                return Error("combat_target", T("不能对敌人使用物品。"));
            if (source.Locked || target.Locked)
                return Error("container_locked", T("物品或目标容器当前被锁定，无法使用。"));
            return null;
        }

        public static ItemUseValidation ValidateActivate(ItemUseReading source)
        {
            if (source?.Item == null)
                return Error("item_not_found", T("物品已不在当前容器树中。"));
            if (source.Combat)
                return Error("combat_item", T("武器、弹药、爆炸物或战斗能力不能由助手使用。"));
            if (source.Locked)
                return Error("container_locked", T("物品当前被锁定，无法使用。"));
            return null;
        }

        private static ItemUseValidation Error(string code, string message)
            => new ItemUseValidation { Code = code, Message = message };
    }
}
