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

        public RibwichInspection AppraiseRibwich(string itemId)
        {
            var item = Resolve(itemId, "RIBWICH_TAG", out var info);
            string[] tags =
            {
                RibwichHelper.RIBWICH_MEAT_TAG, RibwichHelper.RIBWICH_BUN_TAG, RibwichHelper.RIBWICH_PICKLES_TAG,
                RibwichHelper.RIBWICH_WRAP_TAG, RibwichHelper.RIBWICH_DAVRE_LOGO_TAG, RibwichHelper.RIBWICH_RIBWICH_LOGO_TAG,
                RibwichHelper.RIBWICH_FACILITY_NUMBER_TAG, RibwichHelper.RIBWICH_PRODUCTION_MONTH_TAG,
                RibwichHelper.RIBWICH_PRODUCTION_YEAR_TAG, RibwichHelper.RIBWICH_PRODUCTION_TAG, RibwichHelper.RIBWICH_FACILITY_TAG
            };
            int productionTag = item.GetTagReadonly(RibwichHelper.RIBWICH_PRODUCTION_TAG).GetInt();
            int facilityTag = item.GetTagReadonly(RibwichHelper.RIBWICH_FACILITY_TAG).GetInt();
            var result = new RibwichInspection
            {
                Item = info,
                Meat = item.GetTagReadonly(RibwichHelper.RIBWICH_MEAT_TAG).GetInt(),
                Bun = item.GetTagReadonly(RibwichHelper.RIBWICH_BUN_TAG).GetInt(),
                Pickles = item.GetTagReadonly(RibwichHelper.RIBWICH_PICKLES_TAG).GetInt(),
                Wrap = item.GetTagReadonly(RibwichHelper.RIBWICH_WRAP_TAG).GetInt(),
                DavreLogo = item.GetTagReadonly(RibwichHelper.RIBWICH_DAVRE_LOGO_TAG).GetInt(),
                RibwichLogo = item.GetTagReadonly(RibwichHelper.RIBWICH_RIBWICH_LOGO_TAG).GetInt(),
                FacilityNumber = item.GetTagReadonly(RibwichHelper.RIBWICH_FACILITY_NUMBER_TAG).GetInt(),
                ProductionMonth = item.GetTagReadonly(RibwichHelper.RIBWICH_PRODUCTION_MONTH_TAG).GetInt(),
                ProductionYear = item.GetTagReadonly(RibwichHelper.RIBWICH_PRODUCTION_YEAR_TAG).GetInt(),
                BarcodePrinted = productionTag == 0,
                FacilityPrinted = facilityTag == 0,
                Note = T("仅根据检查界面可见的生产日期、包装厂编号、配料、包装和标志判断；opened=true 表示包装已打开，价值按已开封计算。不读取隐藏真伪、不提交估价、不质问客人、不改价。")
            };
            if (!tags.All(item.IsTag) || !KnownRibwichValues(result, productionTag, facilityTag))
            {
                result.VisualAssessment = "unknown";
                result.Note = T("缺少可用的肋明治外观数据或编号超出可见范围，无法判断；未计算基准价。");
                return result;
            }
            result.Opened = result.Wrap != RibwichHelper.RIBWICH_WRAP_CORRECT;
            result.DoubleMeat = result.Meat == RibwichHelper.RIBWICH_MEAT_DOUBLE;
            result.DeluxeEdition = result.Bun == RibwichHelper.RIBWICH_BUN_DELUXE && result.Pickles == RibwichHelper.RIBWICH_PICKLES_EXTRA;
            if (!ValidRibwichDate(result.ProductionMonth, result.ProductionYear)) result.Issues.Add("production_date");
            if (!ValidRibwichFacility(result.FacilityNumber)) result.Issues.Add("facility_number");
            if (result.DavreLogo != 0) result.Issues.Add("davre_logo");
            int period = RibwichProductionPeriod(result.ProductionMonth, result.ProductionYear);
            if (period == 1 && result.RibwichLogo != -1 || period == 2 && result.RibwichLogo != 0) result.Issues.Add("ribwich_logo");
            if (!result.BarcodePrinted) result.Issues.Add("production_print");
            if (!result.FacilityPrinted) result.Issues.Add("facility_print");
            if (!result.DeluxeEdition && !(result.Bun == 0 && result.Pickles == 0)) result.Issues.Add("deluxe");
            result.VisualAssessment = result.Issues.Count == 0 ? "appears_genuine" : "appears_counterfeit";
            result.BaseValue = RibwichHelper.GetCorrectValue(item);
            return result;
        }

        private static bool KnownRibwichValues(RibwichInspection result, int productionTag, int facilityTag)
            => (result.Meat == 0 || result.Meat == 1) &&
               (result.Bun == 0 || result.Bun == 1) &&
               (result.Pickles == 0 || result.Pickles == 1) &&
               result.Wrap >= RibwichHelper.RIBWICH_WRAP_CORRECT && result.Wrap <= RibwichHelper.RIBWICH_WRAP_OPEN_RIGHT &&
               result.DavreLogo >= -1 && result.DavreLogo < RibwichHelper.INCORRECT_DAVRE_LOGO_COUNT &&
               result.RibwichLogo >= -1 && result.RibwichLogo < RibwichHelper.INCORRECT_RIBWICH_LOGO_COUNT &&
               (productionTag == 0 || productionTag == -1) && (facilityTag == 0 || facilityTag == -1);

        private static bool ValidRibwichDate(int month, int year)
            => month >= 1 && month <= 12 &&
               ((year >= RibwichHelper.FIRST_PRODUCTION_DATE_YEAR_MIN && year <= RibwichHelper.FIRST_PRODUCTION_DATE_YEAR_MAX) ||
                (year >= RibwichHelper.SECOND_PRODUCTION_DATE_YEAR_MIN && year <= RibwichHelper.SECOND_PRODUCTION_DATE_YEAR_MAX));

        private static int RibwichProductionPeriod(int month, int year)
        {
            if (month < 1 || month > 12) return 0;
            if (year >= RibwichHelper.FIRST_PRODUCTION_DATE_YEAR_MIN && year <= RibwichHelper.FIRST_PRODUCTION_DATE_YEAR_MAX) return 1;
            if (year >= RibwichHelper.SECOND_PRODUCTION_DATE_YEAR_MIN && year <= RibwichHelper.SECOND_PRODUCTION_DATE_YEAR_MAX) return 2;
            return 0;
        }

        private static bool ValidRibwichFacility(int number)
            => number >= 1 && number <= RibwichHelper.TOTAL_FACILITY_NUMBERS;

        public PlasmaFuelInspection AppraisePlasmaFuel(string itemId)
        {
            var item = Resolve(itemId, "PLASMA_CELL_TAG", out var info);
            string[] tags =
            {
                PlasmaHelper.PLASMA_COLOR, PlasmaHelper.PLASMA_CASING_CONDITION_CRACK, PlasmaHelper.PLASMA_CASING_CONDITION_WEAR,
                PlasmaHelper.PLASMA_CASING_CONDITION_UNCOVERED, PlasmaHelper.PLASMA_CASING_CONDITION_REINFORCED, PlasmaHelper.PLASMA_SEAL
            };
            var result = new PlasmaFuelInspection
            {
                Item = info,
                Color = item.GetTagReadonly(PlasmaHelper.PLASMA_COLOR).GetString(),
                Crack = item.GetTagReadonly(PlasmaHelper.PLASMA_CASING_CONDITION_CRACK).GetString(),
                Wear = item.GetTagReadonly(PlasmaHelper.PLASMA_CASING_CONDITION_WEAR).GetString(),
                Uncovered = item.GetTagReadonly(PlasmaHelper.PLASMA_CASING_CONDITION_UNCOVERED).GetString(),
                Reinforced = item.GetTagReadonly(PlasmaHelper.PLASMA_CASING_CONDITION_REINFORCED).GetString(),
                Seal = item.GetTagReadonly(PlasmaHelper.PLASMA_SEAL).GetString(),
                Note = T("仅根据检查界面可见的颜色、裂纹、磨损、未覆盖、加固和封条判断；没有真伪判定。base_value 使用游戏原生单件鉴定价，不乘堆叠数量，不是交易报价或鉴定报酬。不提交估价、不质问客人、不改价。")
            };
            if (!tags.All(item.IsTag) || !KnownPlasmaValues(result))
            {
                result.ConditionAssessment = "unknown";
                result.Note = T("缺少可用的等离子燃料匣外观数据或状态不在可见范围内，无法判断；未计算基准价。");
                return result;
            }
            if (result.Crack == PlasmaHelper.CRACK) result.Issues.Add("crack");
            if (result.Wear == PlasmaHelper.WEAR) result.Issues.Add("wear");
            else if (result.Wear == PlasmaHelper.WEAR_HEAVY) result.Issues.Add("wear_heavy");
            if (result.Uncovered == PlasmaHelper.UNCOVERED) result.Issues.Add("uncovered");
            if (result.Color == PlasmaHelper.POOR1 || result.Color == PlasmaHelper.POOR2) result.Issues.Add("poor_color");
            result.ConditionAssessment = result.Issues.Any(issue => issue != "poor_color") ? "appears_damaged" : "appears_intact";
            result.BaseValue = PlasmaHelper.GetCorrectValue(item);
            return result;
        }

        private static bool KnownPlasmaValues(PlasmaFuelInspection result)
            => (result.Color == PlasmaHelper.STANDARD || result.Color == PlasmaHelper.HIGH_QUALITY ||
                result.Color == PlasmaHelper.POOR1 || result.Color == PlasmaHelper.POOR2) &&
               (result.Crack == PlasmaHelper.NONE || result.Crack == PlasmaHelper.CRACK) &&
               (result.Wear == PlasmaHelper.NONE || result.Wear == PlasmaHelper.WEAR || result.Wear == PlasmaHelper.WEAR_HEAVY) &&
               (result.Uncovered == PlasmaHelper.NONE || result.Uncovered == PlasmaHelper.UNCOVERED) &&
               (result.Reinforced == PlasmaHelper.NONE || result.Reinforced == PlasmaHelper.REINFORCED) &&
               (result.Seal == PlasmaHelper.LL || result.Seal == PlasmaHelper.ZHONGHUA ||
                result.Seal == PlasmaHelper.REPUBLIC || result.Seal == PlasmaHelper.IGNITION_LAB || result.Seal == PlasmaHelper.NONE);

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
