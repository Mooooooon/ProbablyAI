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
        await Check("customer tools share the registry and are read-only", () =>
        {
            var registry = ToolRegistry.ForHands(new HandsService(new FakeHands()));
            new CustomerService(new FakeCustomer()).Register(registry);
            Assert(registry.Definitions().Count == 10, "customer tools not registered alongside hands");
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
        public CustomerItemCheck CheckItem(string customerId, string itemId)
        {
            Checks++;
            var customer = Current.Customer;
            if (customer == null) throw new HandsException("no_customer", "当前没有已到达的客人。");
            if (customer.Id != customerId) throw new HandsException("customer_changed", "客人已改变。");
            if (itemId != "ore" && itemId != "note-a") throw new HandsException("item_not_found", "物品已失效。");
            CheckedItem = itemId;
            return new CustomerItemCheck
            {
                CustomerId = customerId, ItemId = itemId, CurrentItemValue = 20,
                MatchesRegularPurchaseRules = true, PurchaseBudget = customer.PurchaseBudget,
                WithinRemainingBudget = customer.PurchaseBudget.Enabled ? (bool?)(customer.PurchaseBudget.Remaining >= 20) : null
            };
        }
    }
}
