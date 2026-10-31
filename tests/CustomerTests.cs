using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Customers;
using ProbablyAI.Hands;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunCustomerTests()
    {
        await Check("augmented visitors expose only a quiet reminder", () =>
        {
            var aug = CustomerAug.Create(true);
            Assert(aug != null && aug.IsAug &&
                aug.QuietReminder == "（小声）主人，这个人好像是改造体呢。", "aug hint missing");
            Assert(CustomerAug.Create(false) == null, "ordinary visitor marked as aug");
            var backend = new FakeCustomer(); backend.Current.Customer.Aug = aug;
            var customer = Doc(new CustomerService(backend).Execute("get_current_customer", new JObject()))["data"]["customer"];
            Assert((bool)customer["aug"]["is_aug"] &&
                (string)customer["aug"]["quiet_reminder"] == "（小声）主人，这个人好像是改造体呢。", "aug JSON lost");
            Assert(customer["aug"]["tier"] == null && customer["aug"]["is_scanned"] == null, "hidden aug details leaked");
        });
        await Check("wanted visitors expose only a quiet reminder and suppress reported posters", () =>
        {
            var wanted = CustomerWanted.Create(true, true, false, "wanted1", new Dictionary<string, int>());
            Assert(wanted != null && wanted.IsWanted && wanted.Stealth && wanted.QuietReminder.Contains("通缉令"), "wanted hint missing");
            Assert(CustomerWanted.Create(false, false, false, "", null) == null, "ordinary visitor flagged");
            Assert(CustomerWanted.Create(true, false, true, "wanted1", null) == null, "reported visitor flagged");
            Assert(CustomerWanted.Create(true, false, false, "wanted1", new Dictionary<string, int> { ["wanted1"] = 1 }) == null, "captured poster flagged");
            var backend = new FakeCustomer(); backend.Current.Customer.Wanted = wanted;
            var customer = Doc(new CustomerService(backend).Execute("get_current_customer", new JObject()))["data"]["customer"];
            Assert((bool)customer["wanted"]["is_wanted"] &&
                (string)customer["wanted"]["quiet_reminder"] == "（小声）主人，这个人符合通缉令上的特征呢。", "wanted JSON lost");
            Assert(customer["wanted"]["real_name"] == null && customer["wanted"]["reward"] == null, "hidden identity or bounty leaked");
        });
        await Check("recorder history returns only the current visit with speakers and full text", () =>
        {
            var history = RecorderHistory.CurrentVisit(new[]
            {
                "上一位: 旧对话",
                "---",
                "测试客人: 我想要一块矿石。",
                "Player: 好的，10 点开门。",
                "测试客人: 预算 250，10:30 前要。"
            });
            Assert(history.Count == 3, "history lines lost");
            Assert(history[0].Speaker == "测试客人" && history[0].Text == "我想要一块矿石。", "speaker or text lost");
            Assert(history[1].Speaker == "Player" && history[2].Text == "预算 250，10:30 前要。", "player line or embedded colon lost");
        });
        await Check("recorder history ignores mod-source and malformed lines", () =>
        {
            var history = RecorderHistory.CurrentVisit(new[]
            {
                "<i>这段对话来自模组</i>", "没有冒号", ": 空说话人", "测试客人:", null, "  ", "测试客人: 只在本次。"
            });
            Assert(history.Count == 1 && history[0].Text == "只在本次。", "noise kept or customer line lost");
            Assert(RecorderHistory.CurrentVisit(null).Count == 0, "null history accepted");
        });
        await Check("current customer exposes recorder dialogue history", () =>
        {
            var backend = new FakeCustomer();
            backend.Current.Customer.DialogueHistory = new List<DialogueLine>
            {
                new DialogueLine { Speaker = "测试客人", Text = "我要买食物。" },
                new DialogueLine { Speaker = "Player", Text = "请稍等。" }
            };
            var customer = Doc(new CustomerService(backend).Execute("get_current_customer", new JObject()))["data"]["customer"];
            Assert(((JArray)customer["dialogue_history"]).Count == 2 &&
                (string)customer["dialogue_history"][0]["speaker"] == "测试客人" &&
                (string)customer["dialogue_history"][1]["text"] == "请稍等。", "dialogue history lost");
        });
        await Check("customer tools share the registry and are read-only", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new CustomerService(new FakeCustomer()).Register(registry);
            Assert(registry.Definitions().Count == 12, "customer tools not registered alongside hands");
            foreach (JObject definition in CustomerProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                Assert(!registry.ChangesState(name), "customer read marked as mutation");
                Assert(!(bool)definition["function"]["parameters"]["additionalProperties"], "schema permits extra parameters");
            }
        });
        await Check("customer queries validate arguments before accessing the game", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            Assert(service.Execute("get_current_customer", null).Code == "invalid_arguments", "null arguments");
            Assert(service.Execute("get_current_customer", new JObject { ["execute"] = "sell" }).Code == "invalid_arguments", "extra arguments");
            foreach (var args in new[]
            {
                new JObject(), new JObject { ["customer_id"] = "guest-one" },
                new JObject { ["customer_id"] = 5, ["item_id"] = "ore" },
                new JObject { ["customer_id"] = "guest-one", ["item_id"] = false },
                new JObject { ["customer_id"] = " ", ["item_id"] = "ore" },
                new JObject { ["customer_id"] = "guest-one", ["item_id"] = new string('x', 201) },
                new JObject { ["customer_id"] = "guest-one", ["item_id"] = "ore", ["buy"] = true }
            }) Assert(service.Execute("check_customer_item", args).Code == "invalid_arguments", "malformed ID/extra key accepted");
            Assert(service.Execute("sell_to_customer", new JObject()).Code == "unknown_tool", "unknown tool accepted");
            Assert(backend.Reads == 0 && backend.Checks == 0, "invalid request touched backend");
        });
        await Check("customer absence and arrival never reuse a prior customer's details", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            Assert((string)Doc(service.Execute("get_current_customer", new JObject()))["data"]["customer"]["id"] == "guest-one", "current visit missing");
            foreach (string status in new[] { "absent", "arriving" })
            {
                backend.Current = new CustomerSnapshot { Status = status };
                var result = Doc(service.Execute("get_current_customer", new JObject()));
                Assert((bool)result["success"] && (string)result["data"]["status"] == status, "nonpresent status missing");
                Assert(result["data"]["customer"] == null, "stale customer returned");
                Assert(service.Execute("check_customer_item", CustomerItemArgs()).Code == "no_customer", "absent guest checked");
            }
        });
        await Check("customer changes and stale item failures survive tool dispatch", () =>
        {
            var backend = new FakeCustomer(); var registry = new ToolRegistry();
            new CustomerService(backend).Register(registry);
            backend.Current.Customer.Id = "guest-two";
            Assert(registry.Execute("check_customer_item", CustomerItemArgs()).Code == "customer_changed", "old visit accepted");
            backend.Current.Customer.Id = "guest-one";
            Assert(registry.Execute("check_customer_item", new JObject { ["customer_id"] = "guest-one", ["item_id"] = "expired" }).Code == "item_not_found", "stale item accepted");
            Assert(registry.Execute("check_customer_item", CustomerItemArgs()).Success, "current IDs rejected");
        });
        await Check("batch customer check returns one combined amount and budget verdict", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            var result = Doc(service.Execute("check_customer_items", new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("ore", "note-a") }));
            var data = result["data"];
            Assert((bool)result["success"] && (int)data["count"] == 2, "batch check failed");
            Assert((int)data["total_current_item_value"] == 40 && (int)data["priced_count"] == 2 && (bool)data["within_combined_budget"],
                "combined amount or verdict wrong");
            var entries = (JArray)data["results"];
            Assert(entries.Count == 2 && entries.All(e => e["code"] == null), "per-item results missing");
            Assert(backend.Reads == 1 && backend.Checks == 2, "batch check did not read once and price each item");
        });
        await Check("batch customer check keeps the other prices when one item is stale", () =>
        {
            var result = Doc(new CustomerService(new FakeCustomer()).Execute("check_customer_items",
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("expired", "ore") }));
            var entries = (JArray)result["data"]["results"];
            Assert((string)entries[0]["code"] == "item_not_found" && entries[1]["code"] == null, "stale item hid or broke the rest");
            Assert((int)result["data"]["priced_count"] == 1 && (int)result["data"]["total_current_item_value"] == 20,
                "stale item counted in the total");
        });
        await Check("batch customer check leaves the combined verdict unknown without an enabled budget", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            backend.Current.Customer.PurchaseBudget = new CustomerBudget { Enabled = false };
            var unknown = Doc(service.Execute("check_customer_items", new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("ore", "note-a") }))["data"];
            Assert(unknown["within_combined_budget"] == null, "disabled budget became a verdict");
            backend.Current.Customer.PurchaseBudget = new CustomerBudget { Enabled = true, Remaining = 30 };
            var over = Doc(service.Execute("check_customer_items", new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("ore", "note-a") }))["data"];
            Assert(!(bool)over["within_combined_budget"], "combined budget check ignored the sum");
        });
        await Check("batch customer check validates its list before reading the game", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            var overlong = new JArray(); for (int i = 0; i < 21; i++) overlong.Add("ore");
            foreach (var args in new[]
            {
                new JObject { ["customer_id"] = "guest-one" },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray() },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = overlong },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = "ore" },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("ore", "ore") },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray(5) },
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("ore"), ["buy"] = true }
            }) Assert(service.Execute("check_customer_items", args).Code == "invalid_arguments", "malformed batch accepted");
            Assert(backend.Reads == 0 && backend.Checks == 0, "invalid batch touched the backend");
        });
        await Check("batch customer check reports a changed or absent customer", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            Assert(service.Execute("check_customer_items", new JObject { ["customer_id"] = "guest-two", ["item_ids"] = new JArray("ore") }).Code == "customer_changed",
                "old visit accepted");
            backend.Current = new CustomerSnapshot { Status = "absent" };
            Assert(service.Execute("check_customer_items", new JObject { ["customer_id"] = "guest-two", ["item_ids"] = new JArray("ore") }).Code == "no_customer",
                "absent visitor checked");
            Assert(backend.Checks == 0, "failed batch priced items");
        });
        await Check("batch quote exposes identifiers and counts variety duplicates once", () =>
        {
            var backend = new FakeCustomer();
            backend.Current.Customer.Preferences.RequiresVariety = true;
            backend.Current.Customer.PurchaseBudget = new CustomerBudget { Enabled = true, Remaining = 50 };
            var service = new CustomerService(backend);
            var data = Doc(service.Execute("check_customer_items",
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("note-a", "note-b", "ore") }))["data"];
            var entries = (JArray)data["results"];
            Assert((string)entries[0]["identifier"] == "note" && (string)entries[1]["identifier"] == "note", "identifiers missing");
            Assert(((JArray)data["duplicate_identifiers"]).Select(t => (string)t).SequenceEqual(new[] { "note" }), "duplicate identifier not reported");
            Assert((int)data["total_current_item_value"] == 60 && (int)data["priced_count"] == 3, "plain total wrong");
            Assert((int)data["one_per_identifier_total"] == 40 && (int)data["one_per_identifier_count"] == 2, "variety total counted duplicates");
            Assert((bool)data["within_combined_budget"], "variety affordability used the duplicated total");
        });
        await Check("variety buyers refuse identifiers already bought on this visit", () =>
        {
            var backend = new FakeCustomer();
            backend.Current.Customer.Preferences.RequiresVariety = true;
            backend.Current.Customer.Preferences.PreviouslyBoughtIdentifiers.Add("note");
            var service = new CustomerService(backend);
            var single = Doc(service.Execute("check_customer_item", new JObject { ["customer_id"] = "guest-one", ["item_id"] = "note-a" }))["data"];
            Assert(!(bool)single["matches_regular_purchase_rules"] && (string)single["identifier"] == "note", "bought identifier accepted");
            var data = Doc(service.Execute("check_customer_items",
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("note-a", "ore") }))["data"];
            var entries = (JArray)data["results"];
            Assert(!(bool)entries[0]["matches_regular_purchase_rules"] && (bool)entries[1]["matches_regular_purchase_rules"], "batch ignored the refusal");
            Assert((int)data["priced_count"] == 1 && (int)data["one_per_identifier_total"] == 20, "refused item counted in the variety total");
        });
        await Check("ordinary buyers get no variety-only totals", () =>
        {
            var data = Doc(new CustomerService(new FakeCustomer()).Execute("check_customer_items",
                new JObject { ["customer_id"] = "guest-one", ["item_ids"] = new JArray("note-a", "note-b") }))["data"];
            Assert(data["one_per_identifier_total"] == null && data["one_per_identifier_count"] == null, "variety totals emitted for an ordinary buyer");
            Assert((int)data["total_current_item_value"] == 40, "ordinary total changed");
        });
        await Check("disabled budgets remain unknown and zero budgets remain zero", () =>
        {
            var backend = new FakeCustomer(); var service = new CustomerService(backend);
            backend.Current.Customer.PurchaseBudget = new CustomerBudget { Enabled = false };
            var unknown = Doc(service.Execute("get_current_customer", new JObject()))["data"]["customer"]["purchase_budget"];
            Assert(!(bool)unknown["enabled"] && unknown["remaining"] == null, "disabled budget became cash");
            backend.Current.Customer.PurchaseBudget = new CustomerBudget { Enabled = true, Remaining = 0 };
            var zero = Doc(service.Execute("get_current_customer", new JObject()))["data"]["customer"]["purchase_budget"];
            Assert((bool)zero["enabled"] && (int)zero["remaining"] == 0, "zero budget lost");
            var check = Doc(service.Execute("check_customer_item", CustomerItemArgs()))["data"];
            Assert(!(bool)check["within_remaining_budget"] && (bool)check["value_is_estimate"], "estimate or affordability lost");
        });
        await Check("special requirements preserve repeated barter conditions and mandatory versus bonus gun needs", () =>
        {
            var backend = new FakeCustomer();
            backend.Current.Customer.Barter = new BarterNeeds
            {
                Description = "两件矿石换工具",
                Requirements = { new BarterRequirement { Index = 0, RequiredIdentifier = "ore" }, new BarterRequirement { Index = 1, RequiredIdentifier = "ore" } }
            };
            backend.Current.Customer.GunOrder = new GunOrderNeeds
            {
                BaseReward = 300,
                Mandatory = { new GunRequirement { Tag = "POWER", Comparison = ">", Value = 10 } },
                Bonus = { new GunRequirement { Tag = "GUN_CALIBER_TAG", Comparison = "=", Value = 2 } }
            };
            var customer = Doc(new CustomerService(backend).Execute("get_current_customer", new JObject()))["data"]["customer"];
            Assert(((JArray)customer["barter"]["requirements"]).Count == 2, "duplicate requirements collapsed");
            Assert((string)customer["gun_order"]["mandatory"][0]["comparison"] == ">" && (string)customer["gun_order"]["bonus"][0]["comparison"] == "=", "special comparison changed");
            Assert((int)customer["purchase_budget"]["remaining"] == 250 && (int)customer["gun_order"]["base_reward"] == 300, "reward mixed with budget");
        });
        await Check("customer backend failures are actionable and do not reveal exception contents", () =>
        {
            var backend = new FakeCustomer { Failure = new Exception("test-only-private-detail") };
            var service = new CustomerService(backend);
            var result = service.Execute("get_current_customer", new JObject());
            Assert(result.Code == "customer_query_failed" && !HandsProtocol.Json(result).Contains("private-detail"), "backend details leaked");
            backend.Failure = new HandsException("store_unavailable", "请返回当铺。");
            Assert(service.Execute("get_current_customer", new JObject()).Code == "store_unavailable", "native availability reason lost");
        });
        await Check("chat queries live customer data and checks a real nested item without moving it", async () =>
        {
            var customerBackend = new FakeCustomer(); var handsBackend = new FakeHands();
            var registry = ToolRegistry.ForHands(new HandsService(handsBackend));
            new CustomerService(customerBackend).Register(registry);
            int round = 0;
            var transport = new RegistryTransport((messages, tools, token) =>
            {
                Assert(tools.Any(t => (string)t["function"]["name"] == "get_current_customer") && tools.Any(t => (string)t["function"]["name"] == "check_customer_item"), "model missing customer tools");
                switch (round++)
                {
                    case 0: return Call("guest-query", "get_current_customer", new JObject());
                    case 1:
                        var visitor = JObject.Parse((string)messages.Last["content"])["data"]["customer"];
                        Assert((string)visitor["id"] == "guest-one" && (int)visitor["purchase_budget"]["remaining"] == 250, "live visitor not sent to model");
                        return Call("nested-find", "find_items", new JObject { ["query"] = "笔记", ["container_id"] = "documents" });
                    case 2:
                        var item = JObject.Parse((string)messages.Last["content"])["data"]["page"]["entries"][0];
                        Assert((string)item["container_id"] == "mentor", "nested item location lost");
                        return Call("guest-check", "check_customer_item", new JObject { ["customer_id"] = "guest-one", ["item_id"] = (string)item["id"] });
                    case 3:
                        var result = JObject.Parse((string)messages.Last["content"]);
                        Assert((bool)result["success"] && (bool)result["data"]["within_remaining_budget"], "check result not sent to model");
                        return new ChatTurn { Text = "客人预算 250；这份笔记符合普通购买偏好，目前估值在预算内。" };
                    default: throw new Exception("unexpected round");
                }
            });
            var response = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "当前客人预算多少？档案架中有他愿意买的笔记吗？", null, default);
            Assert(response.ToolCalls == 3 && response.Operations.Count == 0 && handsBackend.Moves == 0, "read-only query mutated inventory");
            Assert(customerBackend.CheckedItem == "note-a", "item ID not forwarded exactly");
        });
        await Check("follow-up chat reads a changed budget instead of returning cached tool data", async () =>
        {
            var backend = new FakeCustomer(); var registry = new ToolRegistry();
            new CustomerService(backend).Register(registry);
            int round = 0; var memory = new ConversationMemory();
            var transport = new FakeTransport((messages, token) =>
            {
                int step = round++;
                if (step % 2 == 0) return Call("budget-" + step, "get_current_customer", new JObject());
                int expected = step == 1 ? 250 : 40;
                Assert((int)JObject.Parse((string)messages.Last["content"])["data"]["customer"]["purchase_budget"]["remaining"] == expected, "cached budget returned");
                return new ChatTurn { Text = "剩余预算 " + expected };
            });
            var agent = new HandsAgent(transport, registry);
            await agent.RunAsync(new ConnectionOptions(), "客人预算多少？", null, default, memory);
            backend.Current.Customer.PurchaseBudget.Remaining = 40;
            var followup = await agent.RunAsync(new ConnectionOptions(), "现在还有多少预算？", null, default, memory);
            Assert(followup.Reply.EndsWith("40") && backend.Reads == 2, "budget not refreshed");
        });
    }

    private static JObject CustomerItemArgs() => new JObject { ["customer_id"] = "guest-one", ["item_id"] = "ore" };

    private sealed class FakeCustomer : ICustomerBackend
    {
        public CustomerSnapshot Current = new CustomerSnapshot
        {
            Status = "present",
            Customer = new CustomerInfo
            {
                Id = "guest-one", Name = "测试客人", Intent = "BUY",
                PurchaseBudget = new CustomerBudget { Enabled = true, Remaining = 250 },
                Preferences = new CustomerPreferences { BuyingTags = { "ALL_TYPE" } }
            }
        };
        public int Reads, Checks;
        public string CheckedItem;
        public Exception Failure;
        public CustomerSnapshot Capture() { Reads++; if (Failure != null) throw Failure; return Current; }
        private static bool Known(string itemId) => itemId == "ore" || itemId == "note-a" || itemId == "note-b";
        private static string IdentifierOf(string itemId) => itemId == "note-a" || itemId == "note-b" ? "note" : itemId;
        private static bool Refused(CustomerInfo customer, string identifier)
            => customer.Preferences.RequiresVariety && customer.Preferences.PreviouslyBoughtIdentifiers.Contains(identifier);
        public CustomerItemCheck CheckItem(string customerId, string itemId)
        {
            Checks++;
            var customer = Current.Customer;
            if (customer == null) throw new HandsException("no_customer", "当前没有已到达的客人。");
            if (customer.Id != customerId) throw new HandsException("customer_changed", "客人已改变。");
            if (!Known(itemId)) throw new HandsException("item_not_found", "物品已失效。");
            CheckedItem = itemId;
            string identifier = IdentifierOf(itemId);
            return new CustomerItemCheck
            {
                CustomerId = customerId, ItemId = itemId, Identifier = identifier, CurrentItemValue = 20,
                MatchesRegularPurchaseRules = !Refused(customer, identifier), PurchaseBudget = customer.PurchaseBudget,
                WithinRemainingBudget = customer.PurchaseBudget.Enabled ? (bool?)(customer.PurchaseBudget.Remaining >= 20) : null
            };
        }
        public CustomerItemsCheck CheckItems(string customerId, IReadOnlyList<string> itemIds)
        {
            var snapshot = Capture();
            var customer = snapshot.Customer;
            if (customer == null) throw new HandsException("no_customer", "当前没有已到达的客人。");
            if (customer.Id != customerId) throw new HandsException("customer_changed", "客人已改变。");
            var result = new CustomerItemsCheck { CustomerId = customerId, Count = itemIds.Count, PurchaseBudget = customer.PurchaseBudget };
            var identifiers = new Dictionary<string, int>(StringComparer.Ordinal);
            var unique = customer.Preferences.RequiresVariety ? new HashSet<string>(StringComparer.Ordinal) : null;
            long uniqueTotal = 0;
            int uniqueCount = 0;
            foreach (var itemId in itemIds)
            {
                // A stale ID must not hide the other prices, matching the native batch behavior.
                if (!Known(itemId))
                {
                    result.Results.Add(new CustomerItemCheck { ItemId = itemId, Code = "item_not_found", Message = "物品已失效。" });
                    continue;
                }
                Checks++;
                CheckedItem = itemId;
                string identifier = IdentifierOf(itemId);
                var check = new CustomerItemCheck
                {
                    CustomerId = customerId, ItemId = itemId, Identifier = identifier, CurrentItemValue = 20,
                    MatchesRegularPurchaseRules = !Refused(customer, identifier), PurchaseBudget = customer.PurchaseBudget
                };
                result.Results.Add(check);
                identifiers.TryGetValue(identifier, out int seen);
                identifiers[identifier] = seen + 1;
                if (seen == 1) result.DuplicateIdentifiers.Add(identifier);
                if (!check.MatchesRegularPurchaseRules) continue;
                result.TotalCurrentItemValue += 20;
                result.PricedCount++;
                if (unique != null && unique.Add(identifier))
                {
                    uniqueTotal += 20;
                    uniqueCount++;
                }
            }
            if (unique != null)
            {
                result.OnePerIdentifierTotal = uniqueTotal;
                result.OnePerIdentifierCount = uniqueCount;
            }
            long combined = unique != null ? uniqueTotal : result.TotalCurrentItemValue;
            int combinedCount = unique != null ? uniqueCount : result.PricedCount;
            result.WithinCombinedBudget = combinedCount > 0 && customer.PurchaseBudget.Enabled && customer.PurchaseBudget.Remaining.HasValue
                ? (bool?)(combined <= customer.PurchaseBudget.Remaining.Value) : null;
            return result;
        }
    }
}
