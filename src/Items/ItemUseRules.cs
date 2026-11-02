using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    public static class ItemUseRules
    {
        public const string WaterTablet = "water_tablet";
        public const string NutrientTablet = "nutrient_tablet";
        public const string UVFilter = "uv_filter";
        public const string WaterRation = "water_ration";

        public static ItemUseValidation Validate(ItemUseReading source, ItemUseReading target)
        {
            if (source?.Item == null || target?.Item == null)
                return Error("item_not_found", T("物品已不在当前容器树中。"));
            if (!string.IsNullOrEmpty(source.Item.Id) && source.Item.Id == target.Item.Id)
                return Error("same_item", T("不能对物品自身使用。"));
            if (string.IsNullOrEmpty(source.Kind))
                return Error("unsupported_item", T("助手只支持对液体容器使用净水药片、营养药片、UV 过滤器或水配给；其他物品请手动使用。"));
            if (!source.Owned || !target.Owned)
                return Error("not_owned", T("只能使用主人自己的物品和容器。"));
            if (!target.LiquidContainer)
                return Error("target_not_liquid", T("目标必须是液体容器（水瓶、水壶等）。"));
            if (!target.SupportedLiquid)
                return Error("unsupported_container", T("目标容器不支持游戏的原生液体数据，无法安全使用。"));
            if (source.Locked || target.Locked)
                return Error("container_locked", T("物品或目标容器当前被锁定，无法使用。"));
            if (target.Wine || target.ChemicalProduct)
                return Error("wrong_target", T("只能在水的液体容器上使用；酒类和化学产品容器不支持。"));
            if (source.Kind != WaterRation && (target.VolumeUl <= 0 || !target.HasWater))
                return Error("target_empty", T("目标容器里没有水，先装入水再使用。"));
            if (source.Kind == WaterRation && source.RequiredUl > 0 && target.FreeUl < source.RequiredUl)
                return Error("target_too_small", T("目标容器的剩余空间不足，装不下这份水配给。"));
            return null;
        }

        private static ItemUseValidation Error(string code, string message)
            => new ItemUseValidation { Code = code, Message = message };
    }
}
