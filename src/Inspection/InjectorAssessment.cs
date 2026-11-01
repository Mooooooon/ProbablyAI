using System;
using System.Collections.Generic;
using System.Linq;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Inspection
{
    // The same visible label data InjectorUI.OnInit renders. Never read *_ISSUE tags or the hidden condition.
    public sealed class InjectorReadings
    {
        public bool HasIndicator { get; set; }
        public bool HasBody { get; set; }
        public bool HasColor { get; set; }
        public bool HasSerial { get; set; }
        public bool HasCompany { get; set; }
        public bool HasModel { get; set; }
        public int Indicator { get; set; }
        public int Body { get; set; }
        public int Color { get; set; }
        public string Serial { get; set; }
        public string Company { get; set; }
        public string Model { get; set; }
    }

    public static class InjectorAssessment
    {
        public const string CorrectCompany = InsInjectorHelper.CORRECT_COMPANY;
        public const string CorrectModel = InsInjectorHelper.CORRECT_MODEL;
        // Prefixes printed in the injector guide as recalled; a recalled injector is not safe to use.
        public static readonly int[] RecalledPrefixes = InsInjectorHelper.RECALLED_PREFIX;
        private const int SerialPrefixMin = 125;
        private const int SerialPrefixMax = 455;

        public static InjectorInspection Evaluate(InjectorReadings readings)
        {
            var result = new InjectorInspection
            {
                Indicator = readings?.Indicator ?? 0,
                Body = readings?.Body ?? 0,
                Color = readings?.Color ?? 0,
                Serial = readings?.Serial ?? string.Empty,
                Company = readings?.Company ?? string.Empty,
                Model = readings?.Model ?? string.Empty,
                Issues = new List<string>()
            };
            // The inspection screen draws the body from INJECTOR_BODY and swaps in INJECTOR_COLOR when the body is hidden.
            bool complete = readings != null && readings.HasIndicator && readings.HasBody && readings.HasColor &&
                readings.HasSerial && readings.HasCompany && readings.HasModel;
            if (!complete)
            {
                result.VisualAssessment = "unknown";
                result.Issues.Add("missing_data");
                result.Note = T("缺少可用的注射器外观数据，无法判断；未读取隐藏异常标签或真伪状态。");
                return result;
            }
            bool supported = (result.Indicator == 0 || result.Indicator == 1 || result.Indicator == 3) &&
                result.Body >= -1 && result.Body <= 3 && result.Color >= -1 && result.Color <= 1 &&
                !(result.Body == -1 && result.Color == -1);
            if (!supported)
            {
                result.VisualAssessment = "unknown";
                result.Issues.Add("unsupported_state");
                result.Note = T("注射器的可见状态超出已知范围，无法判断；未读取隐藏异常标签或真伪状态。");
                return result;
            }

            bool broken = result.Body >= 1;
            bool indicatorUnusable = result.Indicator == 3;
            bool expired = result.Indicator == 1;
            bool recalled = IsRecalledSerial(result.Serial);
            bool serialFormat = !recalled && !IsValidSerialFormat(result.Serial);
            bool companyWrong = !string.Equals(result.Company, CorrectCompany, StringComparison.Ordinal);
            bool modelWrong = !string.Equals(result.Model, CorrectModel, StringComparison.Ordinal);
            bool colorVariant = result.Color >= 0;

            if (broken) result.Issues.Add("body_broken");
            if (indicatorUnusable) result.Issues.Add("indicator_unusable");
            if (expired) result.Issues.Add("indicator_expired");
            if (recalled) result.Issues.Add("recalled_serial");
            if (serialFormat) result.Issues.Add("serial_format");
            if (companyWrong) result.Issues.Add("label_company");
            if (modelWrong) result.Issues.Add("label_model");
            if (colorVariant) result.Issues.Add("color");

            if (broken || indicatorUnusable || recalled)
            {
                result.VisualAssessment = "appears_unusable";
                result.Usable = false;
            }
            else if (expired)
            {
                result.VisualAssessment = "appears_expired";
                result.Usable = false;
            }
            else if (serialFormat || companyWrong || modelWrong || colorVariant)
            {
                result.VisualAssessment = "appears_counterfeit";
                result.Usable = null;
            }
            else
            {
                result.VisualAssessment = "appears_genuine";
                result.Usable = true;
            }
            result.Note = result.VisualAssessment == "appears_genuine"
                ? T("仅根据检查界面可见的针体、指示器、序列号、公司和型号判断，外观符合不保证隐藏真伪；该工具只读，不质问客人、不揭露特性或改价。")
                : T("仅根据检查界面可见的针体、指示器、序列号、公司和型号判断；破损、指示器异常或召回批次的针管不可安全使用。该工具只读，不读取隐藏异常标签、不质问客人或改价；不确定时如实说明。");
            return result;
        }

        public static bool IsRecalledSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial) || serial.Length < 3) return false;
            string prefixText = serial.Substring(0, 3);
            if (!prefixText.All(char.IsDigit)) return false;
            return int.TryParse(prefixText, out int prefix) && RecalledPrefixes.Contains(prefix);
        }

        // Issued serials are seven digits with a prefix inside the valid, non-recalled range.
        public static bool IsValidSerialFormat(string serial)
        {
            if (string.IsNullOrEmpty(serial) || serial.Length != 7) return false;
            if (!serial.All(char.IsDigit)) return false;
            if (!int.TryParse(serial.Substring(0, 3), out int prefix)) return false;
            return prefix >= SerialPrefixMin && prefix <= SerialPrefixMax && !RecalledPrefixes.Contains(prefix);
        }
    }
}
