using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Labels;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static JObject LabelArgs(string tool = "printer") => new JObject { ["tool_item_id"] = tool, ["target_item_id"] = "bottle" };
    private static JObject SetLabelArgs()
    {
        var args = LabelArgs(); args["category_id"] = "quality"; args["condition_id"] = "base"; return args;
    }
    private static JObject RenameArgs(string name)
    {
        var args = LabelArgs("blue"); args["name"] = name; return args;
    }
    private static async Task RunLabelTests()
    {
        await Check("label tools expose strict schemas and classify only mutations as state changes", () =>
        {
            var registry = new ToolRegistry(); new LabelService(new FakeLabels()).Register(registry);
            Assert(registry.Definitions().Count == 3 && !registry.ChangesState("get_label_options") &&
                registry.ChangesState("set_item_label") && registry.ChangesState("rename_item"), "registration incorrect");
            foreach (JObject schema in registry.Definitions())
            {
                var parameters = schema["function"]["parameters"];
                Assert(!(bool)parameters["additionalProperties"] && (string)parameters["type"] == "object", "loose schema");
                Assert(((JArray)parameters["required"]).Count == ((JObject)parameters["properties"]).Count, "required fields missing");
            }
        });
        await Check("label queries expose native choices and custom names without modifying items", () =>
        {
            var backend = new FakeLabels(); var service = new LabelService(backend);
            var queried = Doc(service.Execute("get_label_options", LabelArgs()))["data"];
            Assert((string)queried["labels"][0]["category_id"] == "quality" &&
                (string)queried["labels"][0]["choices"][1]["condition_id"] == "base", "choices lost");
            var named = Doc(service.Execute("get_label_options", LabelArgs("blue")))["data"];
            Assert((string)named["mode"] == "custom_name" && (int)named["name_character_limit"] == 40 &&
                backend.Edits == 0 && backend.Reads == 2, "query mutated state or lost native name limit");
        });
        await Check("malformed label and rename parameters cannot reach the backend", () =>
        {
            var backend = new FakeLabels(); var service = new LabelService(backend);
            foreach (string tool in new[] { "get_label_options", "set_item_label", "rename_item" })
            {
                Func<JObject> valid = () => tool == "set_item_label" ? SetLabelArgs() : tool == "rename_item" ? RenameArgs("水") : LabelArgs();
                foreach (Action<JObject> mutate in new Action<JObject>[]
                {
                    a => a.Remove("tool_item_id"), a => a["tool_item_id"] = false,
                    a => a["target_item_id"] = " ", a => a["tool_item_id"] = new string('x', 201),
                    a => a["target_item_id"] = a["tool_item_id"].DeepClone(), a => a["force"] = true
                })
                {
                    var args = valid(); mutate(args);
                    Assert(service.Execute(tool, args).Code == "invalid_arguments", "malformed request accepted");
                }
                Assert(service.Execute(tool, null).Code == "invalid_arguments", "null accepted");
            }
            foreach (var name in new[] { new string('a', 201), "line\nbreak", "hidden\0char", "  " })
                Assert(service.Execute("rename_item", RenameArgs(name)).Code == "invalid_arguments", "invalid name accepted");
            foreach (string field in new[] { "category_id", "condition_id" })
            {
                var args = SetLabelArgs(); args.Remove(field);
                Assert(service.Execute("set_item_label", args).Code == "invalid_arguments", "missing label ID accepted");
                args[field] = 42;
                Assert(service.Execute("set_item_label", args).Code == "invalid_arguments", "non-string label ID accepted");
            }
            var missingName = RenameArgs("name"); missingName.Remove("name");
            Assert(service.Execute("rename_item", missingName).Code == "invalid_arguments", "missing name accepted");
            Assert(service.Execute("invent_label", LabelArgs()).Code == "unknown_tool" &&
                backend.Reads == 0 && backend.Edits == 0, "invalid request reached backend");
        });
        await Check("label edits verify the returned condition and report before and after values", () =>
        {
            var backend = new FakeLabels(); var service = new LabelService(backend);
            var result = service.Execute("set_item_label", SetLabelArgs()); var data = Doc(result)["data"];
            Assert(result.Success && backend.Edits == 1 && (bool)data["changed"] &&
                (string)data["before"]["labels"][0]["current_condition_id"] == "rust" &&
                (string)data["after"]["labels"][0]["current_condition_id"] == "base", "label change not verified");
            var again = service.Execute("set_item_label", SetLabelArgs());
            Assert(again.Success && !(bool)Doc(again)["data"]["changed"], "already matching label misreported");
            var stale = SetLabelArgs(); stale["condition_id"] = "nonexistent";
            Assert(service.Execute("set_item_label", stale).Code == "invalid_label_option", "stale option accepted");
        });
        await Check("rename preserves requested text and empty name clears custom naming", () =>
        {
            var backend = new FakeLabels(); var service = new LabelService(backend);
            foreach (string name in new[] { "基础水 reserve", "" })
            {
                var result = service.Execute("rename_item", RenameArgs(name));
                Assert(result.Success && ((string)Doc(result)["data"]["after"]["custom_name"] ?? "") == name,
                    "name changed or clear failed");
            }
            Assert(backend.Edits == 2 && backend.CustomName == null, "rename/clear not applied");
            Assert(service.Execute("rename_item", RenameArgs(new string('a', 41))).Code == "name_too_long", "native limit ignored");
        });
        await Check("unverified label or name changes never claim success", () =>
        {
            var backend = new FakeLabels { Apply = false }; var service = new LabelService(backend);
            Assert(service.Execute("set_item_label", SetLabelArgs()).Code == "not_applied", "failed label claimed success");
            Assert(service.Execute("rename_item", RenameArgs("new")).Code == "not_applied", "failed name claimed success");
        });
        await Check("native label failures propagate and unexpected details stay private", () =>
        {
            var backend = new FakeLabels(); var service = new LabelService(backend);
            foreach (string code in new[] { "wrong_tool", "item_not_found", "label_rejected", "label_ui_busy", "rename_rejected" })
            {
                backend.Failure = new HandsException(code, "原生拒绝");
                Assert(service.Execute("set_item_label", SetLabelArgs()).Code == code, "native error lost");
            }
            backend.Failure = new Exception("private-test-only");
            Assert(!HandsProtocol.Json(service.Execute("rename_item", RenameArgs("name"))).Contains("private-test-only"), "details leaked");
        });
        await Check("assistant queries label choices then labels renames and clears through the shared loop", async () =>
        {
            var backend = new FakeLabels(); var registry = new ToolRegistry(); new LabelService(backend).Register(registry);
            int round = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(tools.Count == 3, "model missing label tools");
                switch (round++)
                {
                    case 0: return Call("options", "get_label_options", LabelArgs());
                    case 1:
                        Assert((string)JObject.Parse((string)messages.Last["content"])["data"]["labels"][0]["choices"][1]["condition_id"] == "base", "options not returned");
                        return Call("label", "set_item_label", SetLabelArgs());
                    case 2: return Call("name", "rename_item", RenameArgs("基础水"));
                    case 3: return Call("clear", "rename_item", RenameArgs(""));
                    default: return new ChatTurn { Text = "已改标签并恢复原名。" };
                }
            });
            var result = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "改标签并改名后清除名称", null, default);
            Assert(result.ToolCalls == 4 && result.Operations.Count == 3 && backend.Edits == 3 && backend.Condition == "base", "loop did not complete");
        });
        await Check("label messages follow English locale while custom names keep user wording", () =>
        {
            try
            {
                ProbablyAI.Localization.Text.UseLocale("en");
                var result = new LabelService(new FakeLabels()).Execute("rename_item", RenameArgs("原样 name"));
                Assert(result.Success && result.Message == "The custom name was set and verified." &&
                    (string)Doc(result)["data"]["after"]["custom_name"] == "原样 name", "locale changed user name or left status untranslated");
            }
            finally { ProbablyAI.Localization.Text.UseLocale("zh"); }
        });
    }

    private sealed class FakeLabels : ILabelBackend
    {
        public int Reads, Edits;
        public string Condition = "rust", CustomName;
        public bool Apply = true;
        public Exception Failure;
        public LabelOptions Inspect(string source, string target)
        {
            Reads++;
            if (Failure != null) throw Failure;
            return new LabelOptions
            {
                ToolItemId = source, Item = new ItemInfo { Id = target, Name = CustomName ?? "水壶" },
                Mode = source == "blue" ? "custom_name" : "labels", NameCharacterLimit = 40, CustomName = CustomName,
                Labels = { new EditableLabel { CategoryId = "quality", Name = "水质", CurrentConditionId = Condition,
                    Choices = { new LabelChoice { ConditionId = "rust", Display = "锈水" }, new LabelChoice { ConditionId = "base", Display = "基础水" } } } }
            };
        }
        public LabelChange SetLabel(string source, string target, string category, string condition)
        {
            var before = Inspect(source, target);
            if (category != "quality" || condition != "rust" && condition != "base") throw new HandsException("invalid_label_option", "不支持");
            Edits++; if (Apply) Condition = condition;
            return new LabelChange { Before = before, After = Inspect(source, target), Changed = before.Labels[0].CurrentConditionId != Condition };
        }
        public LabelChange Rename(string source, string target, string name)
        {
            var before = Inspect(source, target);
            if (name.Length > 40) throw new HandsException("name_too_long", "太长");
            Edits++; if (Apply) CustomName = name.Length == 0 ? null : name;
            return new LabelChange { Before = before, After = Inspect(source, target), Changed = before.CustomName != CustomName };
        }
    }
}
