using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Hands;
using ProbablyAI.Inspection;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunInspectionTests()
    {
        await Check("default prompts explain appraisal evidence and preserve customized prompts", () =>
        {
            foreach (string prompt in new[] { AssistantPrompt.ChineseDefault, AssistantPrompt.EnglishDefault })
                Assert(prompt.Contains("appraise_mechanical_hand") && prompt.Contains("inspect_cigarette") &&
                    prompt.Contains("inspect_food_stamp") && prompt.Contains("base_value") &&
                    prompt.Contains("face_value") && prompt.Contains("unknown"), "inspection guidance missing");
            foreach (string previous in new[] { AssistantPrompt.PreviousChineseFreshnessDefault, AssistantPrompt.PreviousEnglishFreshnessDefault,
                AssistantPrompt.PreviousChineseInspectionDefault, AssistantPrompt.PreviousEnglishInspectionDefault })
            {
                Assert(AssistantPrompt.Normalize(previous) == AssistantPrompt.Default, "saved default not upgraded");
                Assert(AssistantPrompt.Normalize(previous + "\nCustom rule") == previous + "\nCustom rule", "custom prompt replaced");
            }
        });
        await Check("inspection tools share the chat registry and are read-only", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new CustomerService(new FakeCustomer()).Register(registry);
            new InspectionService(new GameInspectionBackend(InspectionWorld())).Register(registry);
            Assert(registry.Definitions().Count == 13, "inspection tools missing");
            foreach (JObject definition in InspectionProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                Assert(!registry.ChangesState(name), "inspection marked as mutation");
                Assert(!(bool)definition["function"]["parameters"]["additionalProperties"] &&
                    JToken.DeepEquals(definition["function"]["parameters"]["required"], new JArray("item_id")), "unsafe parameter schema");
            }
        });
        await Check("inspection rejects malformed IDs and extra actions before touching native state", () =>
        {
            var hands = InspectionWorld(); var service = new InspectionService(new GameInspectionBackend(hands));
            foreach (string tool in new[] { "appraise_mechanical_hand", "inspect_cigarette", "inspect_food_stamp" })
                foreach (JObject args in new[] { null, new JObject(), new JObject { ["item_id"] = false },
                    new JObject { ["item_id"] = 12 }, new JObject { ["item_id"] = " " },
                    new JObject { ["item_id"] = new string('x', 201) }, new JObject { ["item_id"] = "hand", ["submit"] = true } })
                    Assert(service.Execute(tool, args).Code == "invalid_arguments", "invalid request accepted");
            Assert(service.Execute("submit_appraisal", InspectionArgs("hand")).Code == "unknown_tool" && hands.Captures == 0, "invalid tool touched game");
        });
        await Check("mechanical hand appraisal delegates value and set rules to native helpers", () =>
        {
            var hands = InspectionWorld(); var item = hands.Items["hand"];
            item.NativeHandValue = 777;
            AppHandHelper.Tactical = true; AppHandHelper.Deluxe = false;
            var result = Doc(new InspectionService(new GameInspectionBackend(hands)).Execute("appraise_mechanical_hand", InspectionArgs("hand")))["data"];
            Assert((int)result["base_value"] == 777 && item.HandValueReads == 1, "native value changed or multiplied by stack count");
            Assert((string)result["material"] == "CARBON_COMPOSITE" && (string)result["control_interface"] == "EMG_PLUS" &&
                (string)result["interface_marking"] == "SYMBOL" && (string)result["condition"] == "NONE", "visual features lost");
            Assert((bool)result["armored"] && (bool)result["bladed"] && (bool)result["tactical_set"] && !(bool)result["deluxe_set"], "native set results lost");
            Assert((string)result["item"]["id"] == "hand" && (int)result["item"]["quantity"] == 3 && hands.Captures == 1, "identity or refresh lost");
            AppHandHelper.Tactical = false; AppHandHelper.Deluxe = true;
            Assert(new GameInspectionBackend(hands).AppraiseMechanicalHand("hand").DeluxeSet, "deluxe result not read");
            AppHandHelper.Deluxe = false;
        });
        await Check("standard cigarette appearance does not expose hidden counterfeit flags", () =>
        {
            var hands = InspectionWorld(); var item = hands.Items["cig"];
            item.Tags.Add("CIGARETTE_SEAL_ISSUE");
            var before = item.Tags.OrderBy(x => x).ToArray();
            var result = new GameInspectionBackend(hands).InspectCigarette("cig");
            Assert(result.VisualAssessment == "appears_genuine" && result.Issues.Count == 0, "hidden flag used as evidence");
            Assert(item.ReadTags.SetEquals(new[] { "CIGARETTE_SEAL", "CIGARETTE_LOGO", "CIGARETTE_LETTERING", "CIGARETTE_BOX" }), "nonvisual data read");
            Assert(before.SequenceEqual(item.Tags.OrderBy(x => x)) && item.HandValueReads == 0, "inspection mutated item");
        });
        await Check("cigarette inspection reports visible defects including missing components", () =>
        {
            foreach (var sample in new[] { ("CIGARETTE_SEAL", -1, "seal"), ("CIGARETTE_SEAL", 1, "seal"),
                ("CIGARETTE_LOGO", -1, "logo"), ("CIGARETTE_LOGO", 4, "logo"),
                ("CIGARETTE_LETTERING", -1, "lettering"), ("CIGARETTE_LETTERING", 7, "lettering"), ("CIGARETTE_BOX", 4, "color") })
            {
                var hands = InspectionWorld(); hands.Items["cig"].TagStates[sample.Item1].IntValue = sample.Item2;
                var result = new GameInspectionBackend(hands).InspectCigarette("cig");
                Assert(result.VisualAssessment == "appears_counterfeit" && result.Issues.SequenceEqual(new[] { sample.Item3 }), "visible defect lost: " + sample);
            }
            var combined = InspectionWorld();
            foreach (var state in combined.Items["cig"].TagStates.Values) state.IntValue = 1;
            Assert(new GameInspectionBackend(combined).InspectCigarette("cig").Issues.Count == 4, "multiple defects collapsed");
        });
        await Check("missing and unsupported cigarette appearance stays unknown", () =>
        {
            var hands = InspectionWorld(); hands.Items["cig"].Tags.Remove("CIGARETTE_SEAL");
            var backend = new GameInspectionBackend(hands);
            Assert(backend.InspectCigarette("cig").VisualAssessment == "unknown", "missing data became genuine");
            hands.Items["cig"].Tags.Add("CIGARETTE_SEAL");
            foreach (var sample in new[] { ("CIGARETTE_SEAL", 2), ("CIGARETTE_LOGO", 5), ("CIGARETTE_LETTERING", 8),
                ("CIGARETTE_BOX", -1), ("CIGARETTE_BOX", 5) })
            {
                var state = hands.Items["cig"].TagStates[sample.Item1]; state.IntValue = sample.Item2;
                Assert(backend.InspectCigarette("cig").VisualAssessment == "unknown", "unsupported variant guessed");
                state.IntValue = 0;
            }
        });
        await Check("food stamp inspection reports face value through visible components only", () =>
        {
            var hands = InspectionWorld(); var item = hands.Items["stamp"];
            item.Tags.Add("STAMP_AMOUNT_ISSUE"); item.Tags.Add("STAMP_CHIP_ISSUE");
            var before = item.Tags.OrderBy(x => x).ToArray();
            var result = new GameInspectionBackend(hands).InspectFoodStamp("stamp");
            Assert(result.VisualAssessment == "appears_genuine" && result.Issues.Count == 0 && result.FaceValue == 160 &&
                result.Seal == 0 && result.Logo == 0 && result.Chip == 0 && result.Lettering == 0, "standard stamp misread");
            Assert(item.ReadTags.SetEquals(new[] { "STAMP_SEAL", "STAMP_LOGO", "STAMP_CHIP", "STAMP_LETTERING", "STAMP_AMOUNT" }), "nonvisual data read");
            Assert(before.SequenceEqual(item.Tags.OrderBy(x => x)) && item.HandValueReads == 0, "inspection mutated item");
            var data = Doc(new InspectionService(new GameInspectionBackend(hands)).Execute("inspect_food_stamp", InspectionArgs("stamp")))["data"];
            Assert((int)data["face_value"] == 160 && (string)data["visual_assessment"] == "appears_genuine" &&
                (string)data["item"]["id"] == "stamp", "protocol lost face value or identity");
        });
        await Check("food stamp inspection flags nonstandard value, seal, logo, chip and lettering defects", () =>
        {
            var cases = new (string Tag, int Value, int Seal, int Amount, string[] Issues)[]
            {
                ("STAMP_LOGO", -1, 0, 160, new[] { "logo" }),
                ("STAMP_LOGO", 5, 0, 160, new[] { "logo" }),
                ("STAMP_LETTERING", -1, 0, 160, new[] { "lettering" }),
                ("STAMP_LETTERING", 8, 0, 160, new[] { "lettering" }),
                ("STAMP_CHIP", -1, 0, 160, new[] { "chip" }),
                ("STAMP_CHIP", 3, 0, 160, new[] { "chip" }),
                ("STAMP_AMOUNT", 50, -1, 50, new[] { "amount" }),
                ("STAMP_AMOUNT", 170, 0, 170, new[] { "amount" }),
                ("STAMP_SEAL", 0, 0, 80, new[] { "seal" })
            };
            foreach (var sample in cases)
            {
                var hands = InspectionWorld(); var item = hands.Items["stamp"];
                item.TagStates[sample.Tag].IntValue = sample.Value;
                item.TagStates["STAMP_SEAL"].IntValue = sample.Seal;
                item.TagStates["STAMP_AMOUNT"].IntValue = sample.Amount;
                var result = new GameInspectionBackend(hands).InspectFoodStamp("stamp");
                Assert(result.VisualAssessment == "appears_counterfeit" && result.Issues.SequenceEqual(sample.Issues),
                    "visible defect lost: " + sample.Tag + "=" + sample.Value);
                Assert(result.FaceValue == sample.Amount, "face value lost");
            }
            var under = InspectionWorld();
            under.Items["stamp"].TagStates["STAMP_AMOUNT"].IntValue = 80;
            under.Items["stamp"].TagStates["STAMP_SEAL"].IntValue = -1;
            Assert(new GameInspectionBackend(under).InspectFoodStamp("stamp").VisualAssessment == "appears_genuine", "valid low-value stamp rejected");
            var combined = InspectionWorld(); var item2 = combined.Items["stamp"];
            item2.TagStates["STAMP_AMOUNT"].IntValue = 50;
            item2.TagStates["STAMP_SEAL"].IntValue = 0;
            item2.TagStates["STAMP_LOGO"].IntValue = 2;
            item2.TagStates["STAMP_CHIP"].IntValue = 1;
            item2.TagStates["STAMP_LETTERING"].IntValue = 2;
            Assert(new GameInspectionBackend(combined).InspectFoodStamp("stamp").Issues
                .SequenceEqual(new[] { "seal", "logo", "chip", "lettering", "amount" }), "multiple defects collapsed");
        });
        await Check("missing and unsupported food stamp appearance stays unknown", () =>
        {
            var hands = InspectionWorld(); hands.Items["stamp"].Tags.Remove("STAMP_CHIP");
            var backend = new GameInspectionBackend(hands);
            Assert(backend.InspectFoodStamp("stamp").VisualAssessment == "unknown", "missing data became genuine");
            hands.Items["stamp"].Tags.Add("STAMP_CHIP");
            foreach (var sample in new[] { ("STAMP_SEAL", 2), ("STAMP_LOGO", 6), ("STAMP_CHIP", 4),
                ("STAMP_LETTERING", 9), ("STAMP_AMOUNT", 0) })
            {
                var state = hands.Items["stamp"].TagStates[sample.Item1]; int original = state.IntValue;
                state.IntValue = sample.Item2;
                Assert(backend.InspectFoodStamp("stamp").VisualAssessment == "unknown", "unsupported value guessed");
                state.IntValue = original;
            }
        });
        await Check("food stamp follow-up reads the current printed value instead of an earlier snapshot", () =>
        {
            var hands = InspectionWorld(); var backend = new GameInspectionBackend(hands);
            Assert(backend.InspectFoodStamp("stamp").FaceValue == 160, "initial face value lost");
            hands.Items["stamp"].TagStates["STAMP_AMOUNT"].IntValue = 80;
            hands.Items["stamp"].TagStates["STAMP_SEAL"].IntValue = -1;
            var result = backend.InspectFoodStamp("stamp");
            Assert(result.FaceValue == 80 && result.VisualAssessment == "appears_genuine" && hands.Captures == 2, "refresh reused cache");
        });
        await Check("locked inspection gameplay returns an actionable failure for all tools", () =>
        {
            var hands = InspectionWorld(); PlayerStore.Instance.isVisualInspectionIntroduced = false;
            var service = new InspectionService(new GameInspectionBackend(hands));
            Assert(service.Execute("appraise_mechanical_hand", InspectionArgs("hand")).Code == "inspection_locked", "locked hand inspected");
            Assert(service.Execute("inspect_cigarette", InspectionArgs("cig")).Code == "inspection_locked", "locked cigarette inspected");
            Assert(service.Execute("inspect_food_stamp", InspectionArgs("stamp")).Code == "inspection_locked", "locked food stamp inspected");
        });
        await Check("inspection refreshes IDs and refuses wrong types and inaccessible items", () =>
        {
            var hands = InspectionWorld(); var service = new InspectionService(new GameInspectionBackend(hands));
            Assert(service.Execute("appraise_mechanical_hand", InspectionArgs("cig")).Code == "wrong_item_type", "cigarette appraised as hand");
            Assert(service.Execute("inspect_cigarette", InspectionArgs("hand")).Code == "wrong_item_type", "hand inspected as cigarette");
            Assert(service.Execute("inspect_food_stamp", InspectionArgs("cig")).Code == "wrong_item_type", "cigarette inspected as food stamp");
            Assert(service.Execute("appraise_mechanical_hand", InspectionArgs("stamp")).Code == "wrong_item_type", "food stamp appraised as hand");
            Assert(service.Execute("inspect_cigarette", InspectionArgs("stamp")).Code == "wrong_item_type", "food stamp inspected as cigarette");
            hands.Items["cig"].Tags.Remove("INSPECTABLE_TAG");
            Assert(service.Execute("inspect_cigarette", InspectionArgs("cig")).Code == "wrong_item_type", "noninspectable item accepted");
            hands.Refresh = () => hands.World.Items.Clear();
            Assert(service.Execute("appraise_mechanical_hand", InspectionArgs("hand")).Code == "item_not_found", "inaccessible object leaked from ID cache");
            Assert(hands.Captures == 7 && hands.Items["hand"].HandValueReads == 0, "refresh bypassed");
        });
        await Check("inspection propagates native availability and thread errors without private details", () =>
        {
            var hands = InspectionWorld(); var service = new InspectionService(new GameInspectionBackend(hands));
            foreach (string code in new[] { "store_unavailable", "wrong_thread", "player_dragging" })
            {
                hands.Failure = new HandsException(code, "请返回当铺。");
                Assert(service.Execute("inspect_cigarette", InspectionArgs("cig")).Code == code, "native guard lost");
            }
            hands.Failure = new Exception("test-only-private-detail");
            var result = service.Execute("inspect_cigarette", InspectionArgs("cig"));
            Assert(result.Code == "inspection_failed" && !HandsProtocol.Json(result).Contains("private-detail"), "backend details leaked");
        });
        await Check("chat receives all appraisal results through the shared tool loop without moving items", async () =>
        {
            var hands = InspectionWorld(); var inventory = new FakeHands();
            var registry = ToolRegistry.ForHands(new HandsService(inventory));
            new CustomerService(new FakeCustomer()).Register(registry);
            new InspectionService(new GameInspectionBackend(hands)).Register(registry);
            int round = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(tools.Count == 13 && tools.Any(t => (string)t["function"]["name"] == "appraise_mechanical_hand") &&
                    tools.Any(t => (string)t["function"]["name"] == "inspect_cigarette") &&
                    tools.Any(t => (string)t["function"]["name"] == "inspect_food_stamp"), "model missing inspection tools");
                switch (round++)
                {
                    case 0: return Call("hand-read", "appraise_mechanical_hand", InspectionArgs("hand"));
                    case 1:
                        Assert((int)JObject.Parse((string)messages.Last["content"])["data"]["base_value"] == 425, "hand result lost");
                        return Call("cig-read", "inspect_cigarette", InspectionArgs("cig"));
                    case 2:
                        Assert((string)JObject.Parse((string)messages.Last["content"])["data"]["visual_assessment"] == "appears_genuine", "cigarette result lost");
                        return Call("stamp-read", "inspect_food_stamp", InspectionArgs("stamp"));
                    default:
                        Assert((int)JObject.Parse((string)messages.Last["content"])["data"]["face_value"] == 160, "food stamp result lost");
                        return new ChatTurn { Text = "机械手鉴定基准价 425，香烟外观符合标准，食品券面值 160 信用点。" };
                }
            });
            var response = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "鉴定机械手、香烟和食品券", null, default);
            Assert(response.ToolCalls == 3 && response.Operations.Count == 0 && inventory.Moves == 0 && hands.Captures == 3, "inspection mutated game or skipped refresh");
        });
        await Check("follow-up inspection reads changed native attributes instead of earlier results", async () =>
        {
            var hands = InspectionWorld(); var registry = new ToolRegistry();
            new InspectionService(new GameInspectionBackend(hands)).Register(registry);
            int round = 0; var memory = new ConversationMemory();
            var transport = new FakeTransport((messages, token) =>
            {
                int step = round++;
                if (step % 2 == 0) return Call("cig-" + step, "inspect_cigarette", InspectionArgs("cig"));
                Assert((string)JObject.Parse((string)messages.Last["content"])["data"]["visual_assessment"] ==
                    (step == 1 ? "appears_genuine" : "appears_counterfeit"), "old appearance reused");
                return new ChatTurn { Text = "已检查" };
            });
            var agent = new HandsAgent(transport, registry);
            await agent.RunAsync(new ConnectionOptions(), "鉴定香烟", null, default, memory);
            hands.Items["cig"].TagStates["CIGARETTE_LOGO"].IntValue = 3;
            await agent.RunAsync(new ConnectionOptions(), "再检查这包香烟", null, default, memory);
            Assert(hands.Captures == 2, "follow-up reused cache");
        });
    }

    private static JObject InspectionArgs(string id) => new JObject { ["item_id"] = id };

    private static GameHandsBackend InspectionWorld()
    {
        PlayerStore.Instance = new PlayerStore { isVisualInspectionIntroduced = true };
        AppHandHelper.Tactical = false; AppHandHelper.Deluxe = false;
        var hands = new GameHandsBackend();
        var hand = new GameItem { NativeHandValue = 425 };
        hand.Tags.UnionWith(new[] { "HAND_TAG", "INSPECTABLE_TAG" });
        string[] tags = { "HAND_MATERIAL", "HAND_INTERFACE", "HAND_INTERFACE_MARKING", "HAND_CONDITION", "HAND_ADDON_ARMORED", "HAND_ADDON_BLADED" };
        string[] values = { "CARBON_COMPOSITE", "EMG_PLUS", "SYMBOL", "NONE", "ARMORED", "BLADED" };
        for (int i = 0; i < tags.Length; i++) hand.TagStates[tags[i]] = new TagState { Value = values[i] };
        var cigarette = new GameItem();
        cigarette.Tags.UnionWith(new[] { "CIGARETTE_TAG", "INSPECTABLE_TAG" });
        foreach (string tag in new[] { "CIGARETTE_SEAL", "CIGARETTE_LOGO", "CIGARETTE_LETTERING", "CIGARETTE_BOX" })
        { cigarette.Tags.Add(tag); cigarette.TagStates[tag] = new TagState(); }
        var stamp = new GameItem();
        stamp.Tags.UnionWith(new[] { "FOOD_STAMP_TAG", "INSPECTABLE_TAG" });
        foreach (string tag in new[] { "STAMP_SEAL", "STAMP_LOGO", "STAMP_CHIP", "STAMP_LETTERING", "STAMP_AMOUNT" })
        { stamp.Tags.Add(tag); stamp.TagStates[tag] = new TagState(); }
        stamp.TagStates["STAMP_AMOUNT"].IntValue = 160;
        hands.Items["hand"] = hand; hands.Items["cig"] = cigarette; hands.Items["stamp"] = stamp;
        hands.World.Items.Add(new ItemInfo { Id = "hand", Name = "机械手", Quantity = 3, ContainerId = "nested", Path = "物品栏 → 箱子 → 机械手" });
        hands.World.Items.Add(new ItemInfo { Id = "cig", Name = "香烟", Quantity = 1, ContainerId = "counter", Path = "柜台 → 香烟" });
        hands.World.Items.Add(new ItemInfo { Id = "stamp", Name = "食品券", Quantity = 1, ContainerId = "customer", Path = "窗口 → 食品券" });
        return hands;
    }
}
