using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Services;

internal static partial class Program
{
    private static async Task RunHandsTests()
    {
        await Check("nested archive search distinguishes identical note names by path and ID", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("find_items", new JObject { ["query"] = "笔记", ["container_id"] = "documents" }));
            var entries = (JArray)result["data"]["page"]["entries"];
            Assert(entries.Count == 2 && entries.Select(i => (string)i["id"]).Distinct().Count() == 2, "same-name notes merged");
            Assert(entries.All(i => ((string)i["path"]).Contains("导师笔记")), "nested paths lost");
        });
        await Check("container tree and direct contents do not flatten a machine's partitions", () =>
        {
            var hands = new HandsService(new FakeHands());
            var root = Doc(hands.Execute("list_container", new JObject { ["container_id"] = "furnace" }));
            Assert(((JArray)root["data"]["children"]).Count == 2, "machine compartments lost");
            Assert(((JArray)root["data"]["items"]["entries"]).Count == 0, "group flattened");
            var search = Doc(hands.Execute("list_containers", new JObject { ["query"] = "input" }));
            Assert((int)search["data"]["page"]["total"] == 1, "role search failed");
        });
        await Check("pagination reports exact totals and leaves names unchanged", () =>
        {
            var hands = new HandsService(new FakeHands());
            var result = Doc(hands.Execute("find_items", new JObject { ["query"] = "笔记", ["limit"] = 1, ["offset"] = 0 }));
            Assert((int)result["data"]["page"]["total"] == 2 && (bool)result["data"]["page"]["has_more"], "pagination metadata");
            Assert(((JArray)result["data"]["page"]["entries"]).Count == 1, "limit ignored");
        });
        await Check("preview does not mutate game state", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("can_move_item", Move("ore", "inventory", "input", 3));
            Assert(result.Success && backend.Moves == 0 && backend.World.Items.First(i => i.Id == "ore").Quantity == 8, "preview mutated");
        });
        await Check("moving one exact nested note leaves its same-name sibling untouched", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("note-a", "mentor", "counter", 1));
            Assert(result.Success && backend.World.Items.First(i => i.Id == "note-a").ContainerId == "counter", "selected note not moved");
            Assert(backend.World.Items.First(i => i.Id == "note-b").ContainerId == "mentor", "sibling moved");
        });
        await Check("zero quantity means one selected stack only", () =>
        {
            var backend = new FakeHands();
            var result = Doc(new HandsService(backend).Execute("move_item", Move("ore", "inventory", "counter", 0)));
            Assert((int)result["data"]["moved"] == 8 && backend.Moves == 1, "zero quantity not scoped to one stack");
        });
        await Check("a partial native transfer is not reported as full success", () =>
        {
            var backend = new FakeHands { TransferLimit = 2 };
            var hands = new HandsService(backend);
            var result = Doc(hands.Execute("move_item", Move("ore", "inventory", "input", 5)));
            Assert(!(bool)result["success"] && (string)result["code"] == "partial", "partial reported success");
            Assert((int)result["data"]["moved"] == 2 && (int)result["data"]["remaining"] == 3, "partial counts wrong");
            Assert(hands.History.Single().Contains("2/5"), "partial missing from audit");
        });
        await Check("zero native movement remains a failed operation", () =>
        {
            var backend = new FakeHands { TransferLimit = 0 };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "trash", 2));
            Assert(!result.Success && result.Code == "not_moved", "zero treated as success");
        });
        await Check("a stale source cannot silently move an item from its new location", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            Assert(hands.Execute("move_item", Move("note-a", "mentor", "counter", 1)).Success, "first move");
            var stale = hands.Execute("move_item", Move("note-a", "mentor", "trash", 1));
            Assert(stale.Code == "source_changed" && backend.Moves == 1, "stale move ran");
        });
        await Check("container into itself or any descendant is rejected", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("bag", "inventory", "bag-inner", 1));
            Assert(result.Code == "container_cycle" && backend.Moves == 0, "cycle accepted");
        });
        await Check("group destination requires a concrete machine compartment", () =>
        {
            var backend = new FakeHands();
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "furnace", 1));
            Assert(result.Code == "choose_compartment" && backend.Moves == 0, "ambiguous machine accepted");
        });
        await Check("locked containers and immovable items remain untouched", () =>
        {
            var backend = new FakeHands();
            backend.World.Containers.First(c => c.Id == "input").InsertLocked = true;
            var hands = new HandsService(backend);
            Assert(hands.Execute("move_item", Move("ore", "inventory", "input", 1)).Code == "container_locked", "locked input accepted");
            backend.World.Items.First(i => i.Id == "ore").Removable = false;
            Assert(hands.Execute("move_item", Move("ore", "inventory", "counter", 1)).Code == "item_locked", "fixed item accepted");
            backend.World.Containers.First(c => c.Id == "mentor").Locked = true;
            Assert(hands.Execute("list_container", new JObject { ["container_id"] = "mentor" }).Code == "container_locked", "locked contents read");
            Assert(backend.Moves == 0, "locked action mutated state");
        });
        await Check("native material and ownership rejection is returned unchanged", () =>
        {
            var backend = new FakeHands { Rejection = "目标不接受此材料 / 所有权不匹配" };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "input", 1));
            Assert(result.Code == "move_rejected" && result.Message == backend.Rejection && backend.Moves == 0, "native rules bypassed");
        });
        await Check("missing IDs, quantities and unknown parameters cannot execute moves", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            foreach (var change in new Action<JObject>[]
            {
                a => a["item_id"] = "old-session-id", a => a.Remove("quantity"), a => a["quantity"] = -1,
                a => a["quantity"] = "3", a => a["quantity"] = 99, a => a["quantity"] = long.MaxValue,
                a => a["arbitrary_code"] = "do something", a => a["source_container_id"] = "物品栏"
            })
            {
                var args = Move("ore", "inventory", "counter", 1); change(args);
                Assert(!hands.Execute("move_item", args).Success, "invalid parameters accepted");
            }
            Assert(backend.Moves == 0, "invalid move executed");
        });
        await Check("open/close uses the same container interface and honors locks", () =>
        {
            var backend = new FakeHands();
            var hands = new HandsService(backend);
            Assert(hands.Execute("open_container", new JObject { ["container_id"] = "mentor" }).Success && backend.Open, "open failed");
            Assert(hands.Execute("close_container", new JObject { ["container_id"] = "mentor" }).Success && !backend.Open, "close failed");
            Assert(hands.Execute("destroy_item", new JObject()).Code == "unknown_tool", "arbitrary actions exposed");
        });
        await Check("native exception does not claim success", () =>
        {
            var backend = new FakeHands { ThrowOnMove = true };
            var result = new HandsService(backend).Execute("move_item", Move("ore", "inventory", "counter", 1));
            Assert(result.Code == "game_error" && !result.Success, "exception hidden as success");
        });
        await Check("all tools declare object schemas without arbitrary extra properties", () =>
        {
            var tools = HandsProtocol.Tools();
            Assert(tools.Count == 8 && tools.Select(t => (string)t["function"]["name"]).Distinct().Count() == 8, "tool set changed unexpectedly");
            foreach (var tool in tools)
            {
                var schema = tool["function"]["parameters"];
                Assert((string)schema["type"] == "object" && !(bool)schema["additionalProperties"], "unsafe/incompatible schema");
                foreach (var required in (JArray)schema["required"]) Assert(schema["properties"][(string)required] != null, "required field missing");
            }
        });
        await Check("OpenAI tool-call response is parsed and reasoning message is preserved", async () =>
        {
            using var http = Respond("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"reasoning_content\":\"provider-state\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}}]}}]}");
            using var client = new OpenAiApiClient(http);
            var turn = await client.CompleteTurnAsync(new ConnectionOptions(), new JArray(), HandsProtocol.Tools(), default);
            Assert(turn.ToolCalls.Single().Name == "list_containers" && (string)turn.Message["reasoning_content"] == "provider-state", "tool/reasoning lost");
        });
        await Check("malformed or duplicate wire tool calls are rejected before execution", async () =>
        {
            foreach (var calls in new[]
            {
                "\"bad\"", "[{}]", "[{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"move_item\",\"arguments\":{}}}]",
                "[{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}},{\"id\":\"a\",\"type\":\"function\",\"function\":{\"name\":\"list_containers\",\"arguments\":\"{}\"}}]"
            })
            {
                using var http = Respond("{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":" + calls + "}}]}");
                using var client = new OpenAiApiClient(http);
                await ExpectAsync<ApiRequestException>(() => client.CompleteTurnAsync(new ConnectionOptions(), new JArray(), HandsProtocol.Tools(), default));
            }
        });
        await Check("agent executes query -> move -> report with matching tool results", async () =>
        {
            var backend = new FakeHands();
            int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                round++;
                if (round == 1) return Call("query", "find_items", new JObject { ["query"] = "笔记" });
                Assert((string)messages[2]["reasoning_content"] == "provider-state", "provider reasoning discarded");
                if (round == 2)
                {
                    Assert((string)messages[3]["tool_call_id"] == "query", "tool result id mismatch");
                    return Call("move", "move_item", Move("note-a", "mentor", "counter", 1));
                }
                Assert((int)JObject.Parse((string)messages.Last["content"])["data"]["moved"] == 1, "actual result missing");
                return new ChatTurn { Text = "已移动 1 份笔记。" };
            });
            var result = await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "移动笔记", null, default);
            Assert(result.ToolCalls == 2 && result.Operations.Count == 1 && backend.Moves == 1, "agent didn't complete tools");
        });
        await Check("agent cancellation between tools stops remaining physical actions", async () =>
        {
            var backend = new FakeHands();
            using var cancellation = new CancellationTokenSource();
            var transport = new FakeTransport((messages, token) =>
            {
                var turn = Call("one", "move_item", Move("note-a", "mentor", "counter", 1));
                turn.ToolCalls = turn.ToolCalls.Concat(new[] { new ChatToolCall { Id = "two", Name = "move_item", Arguments = Move("note-b", "mentor", "trash", 1).ToString() } }).ToArray();
                return turn;
            });
            await ExpectAsync<OperationCanceledException>(() => new HandsAgent(transport, new HandsService(backend))
                .RunAsync(new ConnectionOptions(), "移动两份笔记", _ => { if (backend.Moves == 1) cancellation.Cancel(); }, cancellation.Token));
            Assert(backend.Moves == 1 && backend.World.Items.First(i => i.Id == "note-b").ContainerId == "mentor", "cancelled action still ran");
        });
        await Check("invalid model arguments return a tool error without touching items", async () =>
        {
            var backend = new FakeHands(); int round = 0;
            var transport = new FakeTransport((messages, token) =>
            {
                if (round++ == 0) { var turn = Call("bad", "move_item", new JObject()); turn.ToolCalls[0].Arguments = "not-json"; return turn; }
                Assert((string)JObject.Parse((string)messages.Last["content"])["code"] == "invalid_arguments", "tool error missing");
                return new ChatTurn { Text = "参数无效，未移动物品。" };
            });
            await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "试试", null, default);
            Assert(backend.Moves == 0, "malformed arguments mutated state");
        });
        await Check("agent loop has a finite round budget", async () =>
        {
            var backend = new FakeHands(); int round = 0;
            var transport = new FakeTransport((messages, token) => Call("loop-" + (++round), "list_containers", new JObject()));
            var result = await new HandsAgent(transport, new HandsService(backend)).RunAsync(new ConnectionOptions(), "查看容器", null, default);
            Assert(round == 16 && backend.Moves == 0 && result.Reply.Contains("上限"), "unbounded tool loop");
        });
    }

    private static JObject Move(string item, string source, string target, int quantity)
        => new JObject { ["item_id"] = item, ["source_container_id"] = source, ["destination_container_id"] = target, ["quantity"] = quantity };
    private static JObject Doc(HandsResult value) => JObject.Parse(HandsProtocol.Json(value));
    private static ChatTurn Call(string id, string name, JObject arguments)
    {
        var calls = new[] { new ChatToolCall { Id = id, Name = name, Arguments = arguments.ToString() } };
        return new ChatTurn { ToolCalls = calls, Message = new JObject { ["role"] = "assistant", ["content"] = JValue.CreateNull(), ["reasoning_content"] = "provider-state",
            ["tool_calls"] = new JArray(new JObject { ["id"] = id, ["type"] = "function", ["function"] = new JObject { ["name"] = name, ["arguments"] = arguments.ToString() } }) } };
    }

    private sealed class FakeTransport : IChatTransport
    {
        private readonly Func<JArray, CancellationToken, ChatTurn> reply;
        public FakeTransport(Func<JArray, CancellationToken, ChatTurn> reply) => this.reply = reply;
        public Task<ChatTurn> CompleteTurnAsync(ConnectionOptions options, JArray messages, JArray tools, CancellationToken cancellationToken)
            => Task.FromResult(reply(messages, cancellationToken));
    }

    private sealed class FakeHands : IHandsBackend
    {
        public WorldSnapshot World = new WorldSnapshot();
        public int Moves;
        public int TransferLimit = int.MaxValue;
        public string Rejection;
        public bool ThrowOnMove;
        public bool Open;
        public FakeHands()
        {
            AddContainer("inventory", "物品栏"); AddContainer("counter", "柜台"); AddContainer("customer", "窗口");
            AddContainer("documents", "档案架"); AddContainer("mentor", "导师笔记", "documents"); AddContainer("trash", "垃圾桶");
            AddContainer("furnace", "熔炉", "inventory", "group"); AddContainer("input", "原料", "furnace", role: "input"); AddContainer("output", "成品", "furnace", role: "output");
            AddContainer("bag-inner", "背包内容", "inventory");
            AddItem("ore", "普通矿石", "inventory", 8); AddItem("note-a", "笔记", "mentor", 1); AddItem("note-b", "笔记", "mentor", 1);
            AddItem("bag", "背包", "inventory", 1); World.Items.Last().ContentContainerId = "bag-inner";
        }
        private void AddContainer(string id, string name, string parent = null, string kind = "grid", string role = null)
        {
            var path = parent == null ? name : World.Containers.First(c => c.Id == parent).Path + " → " + name;
            World.Containers.Add(new ContainerInfo { Id = id, Name = name, Path = path, ParentId = parent, Kind = kind, Role = role });
        }
        private void AddItem(string id, string name, string parent, int quantity)
        {
            World.Items.Add(new ItemInfo { Id = id, Name = name, Identifier = name, ContainerId = parent,
                Path = World.Containers.First(c => c.Id == parent).Path + " → " + name, Quantity = quantity, Owned = true, Removable = true });
        }
        public WorldSnapshot Capture() => World;
        public MoveCheck CheckMove(string itemId, string destinationId, int quantity)
            => new MoveCheck { Allowed = Rejection == null, Reason = Rejection, FirstSlotQuantity = quantity };
        public MoveOutcome Move(string itemId, string destinationId, int quantity)
        {
            Moves++;
            if (ThrowOnMove) throw new Exception("native error");
            int moved = Math.Min(quantity, TransferLimit);
            var item = World.Items.First(i => i.Id == itemId);
            if (moved == item.Quantity) item.ContainerId = destinationId;
            else item.Quantity -= moved;
            return new MoveOutcome { Moved = moved };
        }
        public void SetOpen(string containerId, bool open) => Open = open;
    }
}
