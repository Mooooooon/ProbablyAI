using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Documents
{
    public sealed class DocumentService
    {
        private readonly IDocumentBackend backend;
        public DocumentService(IDocumentBackend backend) => this.backend = backend;

        public void Register(ToolRegistry registry)
        {
            foreach (JObject definition in DocumentProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => Execute(name, arguments));
            }
        }

        public HandsResult Execute(string tool, JObject arguments)
        {
            try
            {
                if (tool != "read_document")
                    return HandsResult.Fail("unknown_tool", T("未知的阅读工具。"));
                if (arguments == null || arguments.Properties().Any(p => p.Name != "item_id") ||
                    arguments["item_id"]?.Type != JTokenType.String ||
                    string.IsNullOrWhiteSpace((string)arguments["item_id"]) || ((string)arguments["item_id"]).Length > 200)
                    return HandsResult.Fail("invalid_arguments", T("阅读需要先前查询返回的有效 item_id，且不接受其他参数。"));
                return HandsResult.Ok(backend.Read((string)arguments["item_id"]), T("已读取文档文字，未打开界面或改变物品。"));
            }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("read_failed", T("文档读取失败，请重新查询物品后重试。")); }
        }
    }
}
