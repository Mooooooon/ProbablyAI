using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Inspection
{
    public sealed class WaterLiquid
    {
        public string Identifier { get; set; }
        public string DisplayName { get; set; }
        public int AmountUl { get; set; }
    }

    public sealed class WaterReadings
    {
        public int VolumeUl { get; set; }
        public int CapacityUl { get; set; }
        // -1 means the label or the water-purity feature could not be read.
        public int LabelPurity { get; set; } = -1;
        // Set only when the purity is already visible through testing, exposure or an aquascan.
        public int? TestedPurity { get; set; }
        // The aquascan panel also lists the liquid composition; the strip and label do not.
        public bool ContentsRevealed { get; set; }
        public string LastScan { get; set; }
        public List<WaterLiquid> Liquids { get; set; } = new List<WaterLiquid>();
    }

    public static class WaterAssessment
    {
        private static readonly int[] GradeThresholds = WaterFeatureHelper.WATER_PURITIES;
        private static readonly string[] GradeCodes =
            { "pure", "high_quality", "base", "ghostwater", "rustwater", "gutterflow" };
        private static readonly string[] GradeRanges = WaterFeatureHelper.WATER_VALUE_DISPLAY;

        public static WaterInspection Evaluate(WaterReadings readings)
        {
            if (readings == null)
            {
                return new WaterInspection
                {
                    Empty = true, LabelGrade = "unknown", Grade = "unknown", Potable = "unknown",
                    AssessmentSource = "unknown", Issues = { "missing_data" },
                    Note = T("缺少可用的水容器数据，无法分析；未读取或编造未显示的水质。")
                };
            }
            var result = new WaterInspection
            {
                Empty = readings.VolumeUl <= 0,
                VolumeMl = Math.Max(0, readings.VolumeUl) / 1000,
                CapacityMl = Math.Max(0, readings.CapacityUl) / 1000,
                LastScan = string.IsNullOrWhiteSpace(readings.LastScan) ? null : readings.LastScan.Trim(),
                LabelGrade = "unknown", Grade = "unknown", Potable = "unknown",
                AssessmentSource = "unknown", ContentsRevealed = readings.ContentsRevealed && readings.VolumeUl > 0,
                Issues = new List<string>()
            };
            if (result.CapacityMl > 0)
                result.FillPercent = Math.Round(Math.Min(100d, 100d * readings.VolumeUl / readings.CapacityUl), 1);
            if (readings.ContentsRevealed && readings.Liquids != null && readings.VolumeUl > 0)
            {
                int total = readings.VolumeUl;
                result.Contents = readings.Liquids.Where(liquid => liquid != null && liquid.AmountUl > 0)
                    .Select(liquid => new WaterContent
                    {
                        Liquid = liquid.Identifier, DisplayName = liquid.DisplayName, AmountUl = liquid.AmountUl,
                        AmountMl = liquid.AmountUl / 1000,
                        Percent = Math.Round(100d * liquid.AmountUl / total, 2)
                    }).ToList();
                if (result.Contents.Count == 0) result.ContentsRevealed = false;
            }
            if (result.Empty)
            {
                result.Note = T("容器是空的，没有可分析的水质数据；不推断标签或隐藏成分。");
                return result;
            }

            bool hasLabel = readings.LabelPurity >= 0 && readings.LabelPurity <= 10000;
            if (hasLabel)
            {
                result.LabelGrade = GradeCode(readings.LabelPurity);
                result.LabelPurityRange = GradeRange(readings.LabelPurity);
                result.LabelHazardous = readings.LabelPurity < 9800;
                if (result.LabelHazardous) result.Issues.Add("label_hazardous");
            }
            bool tested = readings.TestedPurity.HasValue && readings.TestedPurity.Value >= 0 &&
                readings.TestedPurity.Value <= 10000;
            if (tested)
            {
                int purity = readings.TestedPurity.Value;
                result.Tested = true;
                result.Purity = purity;
                result.PurityPercent = FormatPurity(purity);
                result.Grade = GradeCode(purity);
                result.GradeRange = GradeRange(purity);
                result.Potable = Potable(purity);
                result.AssessmentSource = "tested";
                if (hasLabel)
                {
                    result.LabelMatchesTest = GradeIndex(readings.LabelPurity) == GradeIndex(purity);
                    if (!result.LabelMatchesTest.Value) result.Issues.Add("label_mismatch");
                }
                result.Note = result.ContentsRevealed
                    ? T("纯度与成分来自已测试或已扫描的数据，是当前容器内的真实状态；仍不提交估价、不改变物品。")
                    : T("纯度来自已测试或已扫描的数据，是当前容器内的真实状态；未扫描时没有可见的成分明细。不提交估价、不改变物品。");
                return result;
            }
            if (hasLabel)
            {
                result.Potable = Potable(readings.LabelPurity);
                result.AssessmentSource = "label";
                result.Note = T("尚未测试或扫描：这里只是瓶身标签显示的等级，不代表真实水质；不读取隐藏纯度。需要确认时用试纸或水质扫描仪。");
                return result;
            }
            result.Note = T("这个水容器没有可读的标签，也没有已测试或已扫描的纯度；不推测水质。");
            return result;
        }

        public static int GradeIndex(int purity)
        {
            for (int index = 0; index < GradeThresholds.Length; index++)
                if (purity >= GradeThresholds[index]) return index;
            // The game's display names anything below gutterflow as unknown water.
            return -1;
        }

        public static string GradeCode(int purity)
        {
            int index = GradeIndex(purity);
            return index < 0 ? "unknown" : GradeCodes[index];
        }

        public static string GradeRange(int purity)
        {
            int index = GradeIndex(purity);
            return index < 0 ? null : GradeRanges[index];
        }

        public static string Potable(int purity)
        {
            int index = GradeIndex(purity);
            if (index < 0) return "toxic";
            if (index <= 1) return "safe";
            if (index == 2) return "caution";
            if (index == 3 || index == 4) return "unsafe";
            return "toxic";
        }

        public static string FormatPurity(int purity)
            => (purity / 100d).ToString("F2", CultureInfo.InvariantCulture) + "%";
    }
}
