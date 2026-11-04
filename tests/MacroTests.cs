using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using ProbablyAI.Labels;
using ProbablyAI.Machines;
using ProbablyAI.Macros;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunMacroTests()
    {
        await Check("macros discover late registered tools and share schemas and handlers with chat", async () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            var macros = TestMacros(registry);
            int calls = 0;
            registry.Register(ItemUseProtocol.Tool("extension_read", "Extension read",
                new JObject { ["value"] = new JObject { ["type"] = "integer" } }, "value"), args =>
            {
                calls++;
                return args["value"]?.Type == JTokenType.Integer
                    ? HandsResult.Ok(new { value = (int)args["value"] })
                    : HandsResult.Fail("invalid_arguments", "Integer required.");
            });
            var assistantTools = new JArray(registry.Definitions().Where(tool => registry.IsGameplay((string)tool["function"]["name"])));
            var macroTools = Doc(registry.Execute("get_macro", new JObject()))["data"]["tools"];
            Assert(JToken.DeepEquals(assistantTools, macroTools) &&
                macroTools.Any(tool => (string)tool["function"]["name"] == "extension_read"), "tool catalogs diverged");

            var invalid = Definition(MacroCall("extension_read", new JObject { ["value"] = "bad" }));
            var entry = macros.Save(null, null, invalid, false);
            var macroResult = await macros.RunAsync(entry.Id, null, default);
            int round = 0;
            JObject chatResult = null;
            var transport = new RegistryTransport((messages, tools, _) =>
            {
                Assert(JToken.DeepEquals(new JArray(tools.Where(tool => registry.IsGameplay((string)tool["function"]["name"]))), macroTools),
                    "assistant did not receive the shared gameplay catalog");
                if (round++ == 0) return Call("extension", "extension_read", new JObject { ["value"] = "bad" });
                chatResult = JObject.Parse((string)messages.Last["content"]);
                return new ChatTurn { Text = "invalid argument reported" };
            });
            await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "test extension", null, default);
            Assert(calls == 2 && (string)chatResult["code"] == macroResult.Code && macroResult.Message.Contains((string)chatResult["message"]),
                "the two paths used different validation");

            var replacement = new ToolRegistry();
            var replacementMacros = new MacroService(macros.Store, replacement);
            replacementMacros.Register();
            var unavailable = await replacement.ExecuteAsync("run_macro", MacroArgs(entry), default);
            Assert(!replacement.Contains("extension_read") && unavailable.Code == "macro_tool_unavailable" &&
                unavailable.Message.Contains("steps[1]") && calls == 2, "removed tool was not reported before execution");
            var recursive = Definition(MacroCall("run_macro", MacroArgs(entry)));
            Assert(Expect<HandsException>(() => macros.Save(null, null, recursive, false)).Code == "macro_tool_unavailable",
                "macro control plane exposed as gameplay");
        });

        await Check("AI drafts require approval, use revisions and preserve the approved version on failed writes", async () =>
        {
            string persisted = null;
            bool failWrite = false;
            var registry = new ToolRegistry();
            var store = new MacroStore(() => null, text =>
            {
                if (failWrite) throw new System.IO.IOException();
                persisted = text;
            });
            var macros = new MacroService(store, registry);
            macros.Register();
            var original = Definition(Report("old"));
            var response = Doc(registry.Execute("save_macro_draft", new JObject { ["definition"] = original }));
            string id = (string)response["data"]["macro_id"];
            long draftRevision = (long)response["data"]["revision"];
            Assert((await registry.ExecuteAsync("run_macro", new JObject { ["macro_id"] = id }, default)).Code == "macro_not_approved",
                "AI draft executed before approval");
            macros.Approve(id, draftRevision);
            var approved = store.Get(id);
            var newer = macros.Save(id, approved.Revision, Definition(Report("new")), true);
            Assert(Expect<HandsException>(() => macros.Approve(id, approved.Revision)).Code == "macro_revision_conflict", "stale revision overwrote draft");
            Assert((string)Doc(await macros.RunAsync(id, null, default))["data"]["reports"][0]["value"] == "old", "pending draft replaced approved macro");
            failWrite = true;
            Assert(Expect<HandsException>(() => macros.Approve(id, newer.Revision)).Code == "macro_save_failed", "failed write was accepted");
            var reloaded = new MacroStore(() => persisted, _ => { });
            Assert(JToken.DeepEquals(store.Get(id).Approved, original) && reloaded.Get(id).Revision == newer.Revision &&
                reloaded.Get(id).Draft != null, "stored macro changed after a failed write");
            var corrupt = new MacroStore(() => "{", _ => throw new Exception("must not write"));
            Assert(corrupt.LoadFailed && Expect<HandsException>(() => corrupt.Save(null, null, original, false)).Code == "macro_store_unavailable",
                "corrupt library was overwritten");
        });

        await Check("local shortcut executes the same macro without transport and shares the busy state", async () =>
        {
            var registry = new ToolRegistry();
            var macros = TestMacros(registry);
            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            registry.RegisterAsync(ItemUseProtocol.Tool("extension_wait", "Wait", new JObject()), async (_, token) =>
            {
                await waiting.Task.WaitAsync(token);
                return HandsResult.Ok(new { volume = 15 });
            });
            var definition = Definition(MacroCall("extension_wait", new JObject(), "waited"),
                new JObject { ["op"] = "report", ["name"] = "volume", ["value"] = Reference("waited.volume") });
            var entry = macros.Save(null, null, definition, false);
            int requests = 0;
            var agent = new HandsAgent(new FakeTransport((_, _) =>
            {
                requests++;
                throw new Exception("offline shortcut contacted model");
            }), registry);
            var local = agent.RunLocalMacroAsync(macros, entry.Id, entry.Revision, new JObject(), null, default);
            Assert(agent.IsRunning, "assistant did not share macro busy state");
            await ExpectAsync<InvalidOperationException>(() => agent.RunAsync(new ConnectionOptions(), "chat", null, default));
            macros.Save(entry.Id, entry.Revision, Definition(Report("changed while running")), false);
            waiting.SetResult(true);
            var result = await local;
            Assert(!agent.IsRunning && requests == 0 && (int)Doc(result)["data"]["reports"][0]["value"] == 15,
                "local run did not use its starting version or called transport");
        });

        await Check("assistant receives one macro result and skips later writes after failure across rounds", async () =>
        {
            var registry = new ToolRegistry();
            var macros = TestMacros(registry);
            int mutations = 0;
            registry.Register(ItemUseProtocol.Tool("test_mutation", "Mutation", new JObject()), _ =>
            {
                mutations++;
                return new HandsResult { Success = true, Code = "partial", Message = "Transferred 2 of 3.", Data = new { moved = 2 } };
            }, changesState: true);
            var entry = macros.Save(null, null, Definition(MacroCall("test_mutation", new JObject()),
                Report("should not report")), false);
            int round = 0;
            var transport = new FakeTransport((messages, _) =>
            {
                switch (round++)
                {
                    case 0: return Call("macro", "run_macro", MacroArgs(entry));
                    case 1:
                        var summary = JObject.Parse((string)messages.Last["content"]);
                        Assert((string)summary["code"] == "partial" && (int)summary["data"]["failed_tool_data"]["moved"] == 2 &&
                            (string)summary["data"]["stopped_at"] == "steps[1]", "missing structured macro result");
                        return Call("write", "test_mutation", new JObject());
                    default:
                        Assert((string)JObject.Parse((string)messages.Last["content"])["code"] == "not_executed",
                            "later writes were not stopped");
                        return new ChatTurn { Text = "stopped" };
                }
            });
            var run = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "run", null, default);
            Assert(run.ToolCalls == 2 && mutations == 1, "macro was retried or a later write ran");
        });

        await Check("history keeps draft call pairing while omitting the draft definition from later requests", async () =>
        {
            var registry = new ToolRegistry();
            TestMacros(registry);
            var definition = Definition(Report("unique draft text"));
            var memory = new ConversationMemory();
            int round = 0;
            var agent = new HandsAgent(new FakeTransport((messages, _) =>
            {
                if (round++ == 0) return Call("draft", "save_macro_draft", new JObject { ["definition"] = definition });
                Assert(messages.ToString().Contains("unique draft text"), "current turn lost its submitted definition");
                return new ChatTurn { Text = "draft saved" };
            }), registry);
            await agent.RunAsync(new ConnectionOptions(), "write a draft", null, default, memory);
            var history = memory.MessagesForModel(registry.ChangesState).ToString();
            Assert(history.Contains("historical_definition_omitted") && !history.Contains("unique draft text") &&
                history.Contains("tool_call_id") && memory.Messages().ToString().Contains("unique draft text"), "draft was not compacted in projected history");
        });

        await Check("macro bindings validate scope and inputs, and paging is explicit", async () =>
        {
            var registry = new ToolRegistry();
            var macros = TestMacros(registry);
            registry.Register(ItemUseProtocol.Tool("paged", "Paged", new JObject()), _ =>
                HandsResult.Ok(new { page = new { has_more = true, entries = new[] { "first", "second" } } }));
            var read = MacroCall("paged", new JObject(), "page");
            var definition = Definition(read);
            var entry = macros.Save(null, null, definition, false);
            Assert((await macros.RunAsync(entry.Id, null, default)).Code == "macro_incomplete_query", "incomplete page silently accepted");
            read["allow_paged"] = true;
            var explicitPage = Definition(read,
                new JObject { ["op"] = "select", ["mode"] = "first", ["from"] = Reference("page.page.entries"), ["as"] = "chosen" },
                new JObject { ["op"] = "report", ["name"] = "choice", ["value"] = Reference("chosen") });
            var updated = macros.Save(entry.Id, entry.Revision, explicitPage, false);
            Assert((string)Doc(await macros.RunAsync(updated.Id, null, default))["data"]["reports"][0]["value"] == "first", "paged selection failed");
            Assert(Expect<HandsException>(() => macros.Save(null, null, Definition(
                new JObject { ["op"] = "report", ["name"] = "invalid", ["value"] = Reference("unknown.field") }), false)).Code == "invalid_macro",
                "unknown reference was saved");
            Assert(Expect<HandsException>(() => macros.Save(null, null, Definition(Report("i-aabbccdd-22")), false)).Code == "invalid_macro",
                "save-specific ID became durable");
            Expect<Newtonsoft.Json.JsonException>(() => MacroDefinition.Parse("{\"version\":1,\"version\":2}"));
        });

        await Check("macro machine waits time out or cancel without performing the next step", async () =>
        {
            var registry = new ToolRegistry();
            var macros = TestMacros(registry);
            int operations = 0;
            CancellationTokenSource cancellation = null;
            new MachineService(_ => null, (_, _, _, _) =>
            {
                operations++;
                return HandsResult.Ok(null);
            }, (_, _) =>
            {
                cancellation?.Cancel();
                return new WaitReading { Data = new { quantity = 0 } };
            }).Register(registry);
            var wait = MachineWaitArgs("output");
            wait["timeout_seconds"] = 1;
            wait["identifier"] = "water_tank";
            wait["quantity"] = 1;
            var entry = macros.Save(null, null, Definition(MacroCall("wait_for_machine", wait),
                MacroCall("operate_machine", new JObject { ["item_id"] = "machine", ["action"] = "blend" })), false);
            var timeout = await macros.RunAsync(entry.Id, null, default);
            using (cancellation = new CancellationTokenSource())
            {
                var stopped = await macros.RunAsync(entry.Id, null, cancellation.Token);
                Assert(timeout.Code == "machine_wait_timeout" && stopped.Code == "macro_cancelled" && operations == 0 &&
                    (string)Doc(stopped)["data"]["stopped_at"] == "steps[1]", "macro did not stop while waiting");
            }
        });

        await Check("macro runs consecutive label operations through the shared label service", async () =>
        {
            var registry = new ToolRegistry();
            var macros = TestMacros(registry);
            var backend = new FakeLabels();
            new LabelService(backend).Register(registry);
            var entry = macros.Save(null, null, Definition(MacroCall("get_label_options", LabelArgs(), "options"),
                MacroCall("set_item_label", SetLabelArgs()), MacroCall("rename_item", RenameArgs("prepared"))), false);
            Assert((await macros.RunAsync(entry.Id, null, default)).Success && backend.Edits == 2 &&
                backend.Condition == "base" && backend.CustomName == "prepared", "label macro failed");
        });

        await RunWaterMacroTests();
    }

    private static MacroService TestMacros(ToolRegistry registry)
    {
        var macros = new MacroService(new MacroStore(() => null, _ => { }), registry);
        macros.Register();
        return macros;
    }

    private static JObject Definition(params JObject[] steps) => new JObject
    {
        ["version"] = 1, ["name"] = "Test macro", ["steps"] = new JArray(steps)
    };

    private static JObject MacroCall(string tool, JObject args, string binding = null)
    {
        var result = new JObject { ["op"] = "call", ["tool"] = tool, ["args"] = args };
        if (binding != null) result["as"] = binding;
        return result;
    }

    private static JObject MacroArgs(MacroStore.Entry entry) => new JObject { ["macro_id"] = entry.Id };
    private static JObject Reference(string name) => new JObject { ["ref"] = name };
    private static JObject Report(string text) => new JObject { ["op"] = "report", ["name"] = "test", ["value"] = text };
}
