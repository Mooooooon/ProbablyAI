using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Documents;
using ProbablyAI.Hands;
using ProbablyAI.Inspection;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunDocumentTests()
    {
        await Check("document tool shares the registry and is read-only", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new CustomerService(new FakeCustomer()).Register(registry);
            new InspectionService(new GameInspectionBackend(InspectionWorld())).Register(registry);
            new DocumentService(new FakeDocument()).Register(registry);
            Assert(registry.Definitions().Count == 16, "document tool missing");
            Assert(!registry.ChangesState("read_document"), "document read marked as mutation");
            var definition = registry.Definitions().First(d => (string)d["function"]["name"] == "read_document");
            Assert(!(bool)definition["function"]["parameters"]["additionalProperties"] &&
                JToken.DeepEquals(definition["function"]["parameters"]["required"], new JArray("item_id")), "unsafe document schema");
        });
        await Check("document tool rejects malformed IDs before reading", () =>
        {
            var backend = new FakeDocument(); var service = new DocumentService(backend);
            foreach (JObject args in new[] { null, new JObject(), new JObject { ["item_id"] = false },
                new JObject { ["item_id"] = 12 }, new JObject { ["item_id"] = " " },
                new JObject { ["item_id"] = new string('x', 201) }, new JObject { ["item_id"] = "note", ["open"] = true } })
                Assert(service.Execute("read_document", args).Code == "invalid_arguments", "invalid request accepted");
            Assert(service.Execute("write_document", new JObject { ["item_id"] = "note" }).Code == "unknown_tool" &&
                backend.Reads == 0, "invalid tool touched backend");
        });
        await Check("document text is returned without changing the item", () =>
        {
            var backend = new FakeDocument
            {
                Content = new DocumentContent
                {
                    Kind = "mentor_note", Title = "导师笔记", Body = "正文内容", Signature = "署名"
                }
            };
            var result = new DocumentService(backend).Execute("read_document", new JObject { ["item_id"] = "note" });
            var data = Doc(result)["data"];
            Assert(result.Success && (string)data["kind"] == "mentor_note" && (string)data["body"] == "正文内容" &&
                (string)data["signature"] == "署名" && backend.Reads == 1, "document text lost");
            backend.Content.Pages.Add(new DocumentPage { Number = 1, Subtitle = "第一页", Body = "页正文" });
            var pages = Doc(new DocumentService(backend).Execute("read_document", new JObject { ["item_id"] = "book" }))["data"]["pages"];
            Assert((int)pages[0]["number"] == 1 && (string)pages[0]["body"] == "页正文", "book pages lost");
        });
        await Check("document tool exposes native read failures without private details", () =>
        {
            var backend = new FakeDocument { Failure = new HandsException("cannot_read", "当前无法阅读。") };
            var service = new DocumentService(backend);
            Assert(service.Execute("read_document", new JObject { ["item_id"] = "note" }).Code == "cannot_read", "native reason lost");
            backend.Failure = new Exception("test-only-private-detail");
            var result = service.Execute("read_document", new JObject { ["item_id"] = "note" });
            Assert(result.Code == "read_failed" && !HandsProtocol.Json(result).Contains("private-detail"), "backend details leaked");
        });
        await Check("chat reads a document through the shared tool loop", async () =>
        {
            var backend = new FakeDocument
            {
                Content = new DocumentContent { Kind = "book", Title = "水交易手册", Text = "第一页内容" }
            };
            var registry = new ToolRegistry();
            new DocumentService(backend).Register(registry);
            int round = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(tools.Any(t => (string)t["function"]["name"] == "read_document"), "model missing document tool");
                switch (round++)
                {
                    case 0: return Call("read-book", "read_document", new JObject { ["item_id"] = "book" });
                    default:
                        var result = JObject.Parse((string)messages.Last["content"]);
                        Assert((string)result["data"]["text"] == "第一页内容", "document result not sent to model");
                        return new ChatTurn { Text = "这本手册第一页写着：第一页内容。" };
                }
            });
            var response = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "这本手册写了什么？", null, default);
            Assert(response.ToolCalls == 1 && backend.Reads == 1, "document read not executed");
        });
    }

    private sealed class FakeDocument : IDocumentBackend
    {
        public DocumentContent Content = new DocumentContent { Kind = "mentor_note", Title = "标题", Body = "正文" };
        public Exception Failure;
        public int Reads;
        public DocumentContent Read(string itemId)
        {
            Reads++;
            if (Failure != null) throw Failure;
            return Content;
        }
    }
}
