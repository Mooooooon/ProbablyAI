using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;

namespace ProbablyAI.Customers
{
    public sealed class CustomerService
    {
        private readonly ICustomerBackend backend;
        public CustomerService(ICustomerBackend backend) => this.backend = backend;

        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in CustomerProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => Execute(name, arguments));
            }
        }

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                if (arguments == null) throw new HandsException("invalid_arguments", "参数必须是 JSON 对象。");
                switch (tool)
                {
                    case "get_current_customer":
                        ValidateKeys(arguments);
                        var snapshot = backend.Capture();
                        return HandsResult.Ok(snapshot, snapshot.Status == "present" ? "已读取当前客人的实时信息。" :
                            snapshot.Status == "arriving" ? "客人尚未到达柜台，请稍后重新查询。" : "当前没有客人。不能沿用上一位客人的预算和需求。");
                    case "check_customer_item":
                        ValidateKeys(arguments, "customer_id", "item_id");
                        return HandsResult.Ok(backend.CheckItem(Text(arguments, "customer_id"), Text(arguments, "item_id")));
                    default:
                        return HandsResult.Fail("unknown_tool", "未知的客人查询工具。");
                }
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("customer_query_failed", "读取客人信息失败，请在客人到达后重新查询。"); }
        }

        private static void ValidateKeys(JObject args, params string[] allowed)
        {
            if (args.Properties().Any(p => !allowed.Contains(p.Name)))
                throw new HandsException("invalid_arguments", "工具包含不支持的参数。");
        }

        private static string Text(JObject args, string key)
        {
            var token = args[key];
            if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token) || ((string)token).Length > 200)
                throw new HandsException("invalid_arguments", key + " 必须是先前查询返回的有效 ID。");
            return (string)token;
        }
    }
}
