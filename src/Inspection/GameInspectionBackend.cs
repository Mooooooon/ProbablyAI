using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Inspection
{
    // Refresh through the shared container tree: same IDs, main thread, locks and session lifetime.
    public sealed class GameInspectionBackend : IInspectionBackend
    {
        private readonly GameHandsBackend hands;
        public GameInspectionBackend(GameHandsBackend hands) => this.hands = hands;

        // The in-game inspection manual lists the only issued denominations; any other printed value is a visible anomaly.
        private static readonly int[] StampDenominations = { 40, 80, 120, 160, 200, 240 };
        private const int StampSealThreshold = 160;

        private GameItem Resolve(string itemId, string typeTag, out ItemInfo info)
        {
            var world = hands.Capture();
            if (PlayerStore.Instance == null || !PlayerStore.Instance.isVisualInspectionIntroduced)
                throw new HandsException("inspection_locked", T("当前存档尚未解锁目视检查，请先完成对应的教学。"));
            info = world.Items.FirstOrDefault(i => i.Id == itemId);
            if (info == null) throw new HandsException("item_not_found", T("物品已不在当前容器树中。"));
            var item = hands.ResolveCurrentItem(itemId);
            if (!item.IsTag("INSPECTABLE_TAG") || !item.IsTag(typeTag))
                throw new HandsException("wrong_item_type", T("此物品不属于该鉴定工具支持的类型。"));
            return item;
        }

        public MechanicalHandInspection AppraiseMechanicalHand(string itemId)
        {
            var item = Resolve(itemId, "HAND_TAG", out var info);
            // These are the same visual attributes read by AppHandUI.OnInit.
            string material = item.GetTagReadonly("HAND_MATERIAL").GetString();
            string control = item.GetTagReadonly("HAND_INTERFACE").GetString();
            string marking = item.GetTagReadonly("HAND_INTERFACE_MARKING").GetString();
            string condition = item.GetTagReadonly("HAND_CONDITION").GetString();
            string armored = item.GetTagReadonly("HAND_ADDON_ARMORED").GetString();
            string bladed = item.GetTagReadonly("HAND_ADDON_BLADED").GetString();
            return new MechanicalHandInspection
            {
                Item = info, Material = material, ControlInterface = control, InterfaceMarking = marking,
                Condition = condition, Armored = armored == "ARMORED", Bladed = bladed == "BLADED",
                TacticalSet = AppHandHelper.IsTacticalSet(material, control, condition, armored, bladed),
                DeluxeSet = AppHandHelper.IsDeluxeSet(material, control, condition),
                BaseValue = AppHandHelper.GetCorrectValue(item),
                Note = T("单件鉴定基准价已包含材质、接口、磨损、附加件和套装效果；不是交易报价或鉴定报酬。未提交估价，未完成委托。")
            };
        }

        public CigaretteInspection InspectCigarette(string itemId)
        {
            var item = Resolve(itemId, "CIGARETTE_TAG", out var info);
            var result = new CigaretteInspection
            {
                Item = info,
                Seal = item.GetTagReadonly("CIGARETTE_SEAL").GetInt(),
                Logo = item.GetTagReadonly("CIGARETTE_LOGO").GetInt(),
                Lettering = item.GetTagReadonly("CIGARETTE_LETTERING").GetInt(),
                Box = item.GetTagReadonly("CIGARETTE_BOX").GetInt(),
                Note = T("仅按检查界面可见外观与标准样本比较；不读取隐藏真伪或异常标签。外观符合不保证隐藏真伪，未揭露特性、质问客人或改价。")
            };
            // Never use *_ISSUE tags or a feature's hidden realCondition as evidence.
            bool known = new[] { "CIGARETTE_SEAL", "CIGARETTE_LOGO", "CIGARETTE_LETTERING", "CIGARETTE_BOX" }.All(item.IsTag) &&
                result.Seal >= -1 && result.Seal < InsCigaretteHelper.SEAL_COUNT &&
                result.Logo >= -1 && result.Logo < InsCigaretteHelper.LOGO_COUNT &&
                result.Lettering >= -1 && result.Lettering < InsCigaretteHelper.LETTERING_COUNT &&
                result.Box >= 0 && result.Box <= InsCigaretteHelper.COLOR_COUNT;
            if (!known) { result.VisualAssessment = "unknown"; return result; }
            if (result.Seal != 0) result.Issues.Add("seal");
            if (result.Logo != 0) result.Issues.Add("logo");
            if (result.Lettering != 0) result.Issues.Add("lettering");
            if (result.Box != 0) result.Issues.Add("color");
            result.VisualAssessment = result.Issues.Count == 0 ? "appears_genuine" : "appears_counterfeit";
            return result;
        }

        public FoodStampInspection InspectFoodStamp(string itemId)
        {
            var item = Resolve(itemId, "FOOD_STAMP_TAG", out var info);
            // The same visible components shown by StampUI.OnInit.
            var result = new FoodStampInspection
            {
                Item = info,
                Seal = item.GetTagReadonly("STAMP_SEAL").GetInt(),
                Logo = item.GetTagReadonly("STAMP_LOGO").GetInt(),
                Chip = item.GetTagReadonly("STAMP_CHIP").GetInt(),
                Lettering = item.GetTagReadonly("STAMP_LETTERING").GetInt(),
                FaceValue = item.GetTagReadonly("STAMP_AMOUNT").GetInt(),
                Note = T("票面面值按检查界面显示的信用点返回，不是交易报价或兑换承诺。防伪标应与 160 信用点及以上的面值同时出现；芯片缺失或损坏时无法在配给站兑换，即使其余外观符合标准。未提交估价、未质问客人、未兑换或改变物品。")
            };
            // Never read the hidden *_ISSUE tags: only the printed components are evidence.
            bool known = new[] { "STAMP_SEAL", "STAMP_LOGO", "STAMP_CHIP", "STAMP_LETTERING", "STAMP_AMOUNT" }.All(item.IsTag) &&
                result.Seal >= -1 && result.Seal < InsStampHelper.STAMP_SEAL_COUNT &&
                result.Logo >= -1 && result.Logo < InsStampHelper.STAMP_LOGO_COUNT &&
                result.Chip >= -1 && result.Chip < InsStampHelper.STAMP_CHIP_COUNT &&
                result.Lettering >= -1 && result.Lettering < InsStampHelper.STAMP_LETTERING_COUNT &&
                result.FaceValue > 0;
            if (!known) { result.VisualAssessment = "unknown"; return result; }
            // Genuine stamps show no logo, lettering or chip variant, and a seal exactly when the face value is high enough.
            if (result.Seal != (result.FaceValue >= StampSealThreshold ? 0 : -1)) result.Issues.Add("seal");
            if (result.Logo != 0) result.Issues.Add("logo");
            if (result.Chip != 0) result.Issues.Add("chip");
            if (result.Lettering != 0) result.Issues.Add("lettering");
            if (!StampDenominations.Contains(result.FaceValue)) result.Issues.Add("amount");
            result.VisualAssessment = result.Issues.Count == 0 ? "appears_genuine" : "appears_counterfeit";
            return result;
        }
    }
}
