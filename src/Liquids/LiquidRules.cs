using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Liquids
{
    // Shared validation for the native adapter and the tests. It only reads the plain
    // snapshot DTOs, so it can run without Unity or a loaded save.
    public static class LiquidRules
    {
        private const int WinePourVolumeUl = 750000;

        public static LiquidValidation ValidatePour(LiquidReading source, LiquidReading target, bool requireFullTransfer = false)
        {
            var sourceError = ValidateContainer(source);
            if (sourceError != null) return sourceError;
            var targetError = ValidateContainer(target);
            if (targetError != null) return targetError;
            if (!string.IsNullOrEmpty(source.Item.Id) && source.Item.Id == target.Item.Id)
                return Error("same_item", T("不能把容器倒进它自己。"));
            if (source.Empty) return Error("source_empty", T("源容器是空的，没有液体可倒。"));
            if (target.Full) return Error("target_full", T("目标容器已满，无法再接收液体。"));
            // Finished wine is not transferred proportionally: the game pours a fixed 750 ml.
            if (source.WineReady && target.FreeUl < WinePourVolumeUl)
                return Error("target_too_small", T("成品酒一次需要 750 ml 空间，目标容器的剩余空间不足。"));
            if (requireFullTransfer && target.FreeUl < source.VolumeUl)
                return Error("target_too_small", T("目标容器的剩余空间不足以接收全部液体。"));
            return null;
        }

        public static LiquidValidation ValidateDump(LiquidReading source)
        {
            var error = ValidateContainer(source);
            if (error != null) return error;
            if (source.Empty) return Error("source_empty", T("容器里没有液体可倒掉。"));
            return null;
        }

        public static LiquidValidation ValidateFill(LiquidReading target)
        {
            var error = ValidateContainer(target);
            if (error != null) return error;
            if (target.Full) return Error("target_full", T("容器已经满了，无法再接水。"));
            return null;
        }

        internal static LiquidValidation ValidateContainer(LiquidReading reading)
        {
            if (reading?.Item == null)
                return Error("item_not_found", T("物品已不在当前容器树中。"));
            if (!reading.Container)
                return Error("not_liquid_container", T("只能操作液体容器（水瓶、水壶、酒瓶等）。"));
            if (!reading.Supported)
                return Error("unsupported_container", T("这个容器不支持游戏的原生液体数据，无法安全倒取。"));
            if (!reading.Owned)
                return Error("not_owned", T("只能操作主人自己的液体容器。"));
            return null;
        }

        private static LiquidValidation Error(string code, string message)
            => new LiquidValidation { Code = code, Message = message };
    }
}
