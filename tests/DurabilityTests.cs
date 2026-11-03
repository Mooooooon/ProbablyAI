using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;

internal static partial class Program
{
    private static GameItem DurableItem(int displayed, int real, int maximum)
    {
        var item = new GameItem();
        item.Tags.Add("DURABILITY_TAG");
        // Native numeric tags can store valid values while not enabled.
        item.TagStates["DURABILITY_TAG"] = new TagState { IntValue = real };
        item.TagStates["DURABILITY_FAKE_TAG"] = new TagState { IntValue = displayed };
        item.TagStates["MAX_DURABILITY_TAG"] = new TagState { IntValue = maximum };
        item.itemFeatures.Add(new ItemFeature { identifier = "DURABILITY_TAG" });
        return item;
    }

    private static async Task RunDurabilityTests()
    {
        await Check("generic durability reports public values without reading hidden real durability", () =>
        {
            var item = DurableItem(75, 12, 200);
            var result = GameItemDurability.Read(item);
            Assert(result.Current == 75 && result.Maximum == 200 && result.RemainingPercent == 37.5 &&
                !result.Depleted && result.AssessmentSource == "displayed", "public durability incorrect");
            Assert(!item.ReadTags.Contains("DURABILITY_TAG"), "hidden real durability was read");
            item.TagStates["DURABILITY_FAKE_TAG"].IntValue = 0;
            Assert(GameItemDurability.Read(item).Depleted, "public exhaustion lost");
        });
        await Check("discovered or exposed durability reports the tested value including zero", () =>
        {
            foreach (bool exposed in new[] { false, true })
            {
                var item = DurableItem(80, 0, 100);
                item.itemFeatures[0].Exposed = exposed;
                item.itemFeatures[0].Discovered = !exposed;
                item.itemFeatures[0].Hidden = true;
                var result = GameItemDurability.Read(item);
                Assert(result.Current == 0 && result.Depleted && result.RemainingPercent == 0 &&
                    result.AssessmentSource == "tested", "tested exhaustion lost");
                Assert(!item.ReadTags.Contains("DURABILITY_FAKE_TAG"), "tested query used public claim");
            }
        });
        await Check("durability omits absent hidden disabled and invalid data", () =>
        {
            Assert(GameItemDurability.Read(null) == null && GameItemDurability.Read(new GameItem()) == null, "non-durable item fabricated");
            foreach (string reason in new[] { "hidden", "not-shown", "disabled", "missing-feature" })
            {
                var item = DurableItem(50, 10, 100);
                if (reason == "hidden") item.itemFeatures[0].Hidden = true;
                if (reason == "not-shown") item.itemFeatures[0].InitiallyShown = false;
                if (reason == "disabled") item.itemFeatures[0].isDisabled = true;
                if (reason == "missing-feature") item.itemFeatures.Clear();
                Assert(GameItemDurability.Read(item) == null && item.ReadTags.Count == 0, "unavailable feature leaked");
            }
            foreach (var pair in new[] { (-1, 100), (101, 100), (0, 0), (1, -1) })
                Assert(GameItemDurability.Read(DurableItem(pair.Item1, 5, pair.Item2)) == null, "invalid reading fabricated");
            var large = GameItemDurability.Read(DurableItem(int.MaxValue, 1, int.MaxValue));
            Assert(large.RemainingPercent == 100, "percentage overflow");
        });
        await Check("inspect search and container listing expose generic durability and refresh exhaustion", () =>
        {
            var backend = new FakeHands();
            var target = backend.World.Items.First(i => i.Id == "note-a");
            var native = DurableItem(40, 40, 100);
            target.Durability = GameItemDurability.Read(native);
            var hands = new HandsService(backend);
            var inspected = Doc(hands.Execute("inspect_item", new JObject { ["item_id"] = target.Id }))["data"];
            Assert((int)inspected["durability"]["current"] == 40, "inspect lost durability");
            var found = Doc(hands.Execute("find_items", new JObject { ["query"] = target.Name }));
            Assert(((JArray)found["data"]["page"]["entries"]).Any(i => (string)i["id"] == target.Id &&
                (int)i["durability"]["maximum"] == 100), "search lost durability");
            var listed = Doc(hands.Execute("list_container", new JObject { ["container_id"] = target.ContainerId }));
            Assert(((JArray)listed["data"]["items"]["entries"]).Any(i => (string)i["id"] == target.Id &&
                (string)i["durability"]["assessment_source"] == "displayed"), "list lost durability");
            native.TagStates["DURABILITY_FAKE_TAG"].IntValue = 0;
            target.Durability = GameItemDurability.Read(native);
            var refreshed = Doc(hands.Execute("inspect_item", new JObject { ["item_id"] = target.Id }))["data"]["durability"];
            Assert((bool)refreshed["depleted"] && (int)refreshed["current"] == 0, "query reused stale durability");
            Assert(JObject.Parse(HandsProtocol.Json(new ItemInfo { Id = "plain" }))["durability"] == null, "absent durability serialized as zero");
        });
    }
}
