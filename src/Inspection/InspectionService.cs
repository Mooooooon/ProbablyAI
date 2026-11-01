using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Inspection
{
    public sealed class InspectionService
    {
        private readonly IInspectionBackend backend;
        public InspectionService(IInspectionBackend backend) => this.backend = backend;

        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in InspectionProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => Execute(name, arguments));
            }
        }

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                if (tool != "appraise_mechanical_hand" && tool != "appraise_ribwich" && tool != "appraise_plasma_fuel" &&
                    tool != "inspect_cigarette" && tool != "inspect_food_stamp" && tool != "inspect_injector" &&
                    tool != "analyze_water")
                    return HandsResult.Fail("unknown_tool", T("未知的鉴定工具。"));
                if (arguments == null || arguments.Properties().Any(p => p.Name != "item_id") ||
                    arguments["item_id"]?.Type != JTokenType.String ||
                    string.IsNullOrWhiteSpace((string)arguments["item_id"]) || ((string)arguments["item_id"]).Length > 200)
                    return HandsResult.Fail("invalid_arguments", T("鉴定需要先前查询返回的有效 item_id，且不接受其他参数。"));
                string itemId = (string)arguments["item_id"];
                if (tool == "appraise_mechanical_hand")
                    return HandsResult.Ok(backend.AppraiseMechanicalHand(itemId), T("已读取机械手的鉴定特征和基准价，未提交估价。"));
                if (tool == "appraise_ribwich")
                    return HandsResult.Ok(backend.AppraiseRibwich(itemId), T("已读取肋明治的外观和鉴定基准价，未提交估价。"));
                if (tool == "appraise_plasma_fuel")
                    return HandsResult.Ok(backend.AppraisePlasmaFuel(itemId), T("已读取等离子燃料匣的外观和鉴定基准价，未提交估价。"));
                if (tool == "inspect_cigarette")
                    return HandsResult.Ok(backend.InspectCigarette(itemId), T("已检查香烟外观，未质问客人或改变物品特性。"));
                if (tool == "inspect_food_stamp")
                    return HandsResult.Ok(backend.InspectFoodStamp(itemId), T("已检查食品券外观和票面面值，未兑换、质问客人或改变物品。"));
                if (tool == "inspect_injector")
                    return HandsResult.Ok(backend.InspectInjector(itemId), T("已检查注射器外观，未读取隐藏异常、质问客人或揭露特性。"));
                return HandsResult.Ok(backend.AnalyzeWater(itemId), T("已分析水容器的可见数据，未读取未显示纯度或改变容器。"));
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("inspection_failed", T("鉴定信息读取失败，请重新查询物品后重试。")); }
        }
    }
}
