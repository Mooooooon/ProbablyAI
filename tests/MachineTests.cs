using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Machines;
using ProbablyAI.Services;
using ProbablyAI.Tools;

internal static partial class Program
{
    private static async Task RunMachineTests()
    {
        await Check("machine tools validate command values and preserve native errors", () =>
        {
            var registry = new ToolRegistry();
            int calls = 0;
            new MachineService(_ => HandsResult.Ok(null), (id, action, recipe, target) =>
            {
                calls++;
                Assert(id == "machine" && action == "set_recipe" && recipe == "caffeine_pill" && !target.HasValue,
                    "machine command values were lost");
                throw new HandsException("machine_recipe_not_ready", "not ready");
            }, (_, _) => throw new InvalidOperationException()).Register(registry);
            Assert(registry.ChangesState("operate_machine") && !registry.ChangesState("inspect_machine") &&
                !registry.ChangesState("wait_for_machine"), "machine mutation metadata is wrong");

            foreach (var args in new[]
            {
                new JObject { ["item_id"] = "machine", ["action"] = "set_recipe", ["value"] = 20 },
                new JObject { ["item_id"] = "machine", ["action"] = "set_feed_target", ["value"] = 23 },
                new JObject { ["item_id"] = "machine", ["action"] = "set_feed_target", ["value"] = "25" },
                new JObject { ["item_id"] = "machine", ["action"] = "blend", ["value"] = JValue.CreateNull() },
                new JObject { ["item_id"] = "machine", ["action"] = "blend", ["repeat"] = 3 }
            })
                Assert(registry.Execute("operate_machine", args).Code == "invalid_arguments", "invalid machine command accepted");
            Assert(calls == 0, "invalid command reached backend");

            var result = registry.Execute("operate_machine", new JObject
            {
                ["item_id"] = "machine", ["action"] = "set_recipe", ["value"] = "caffeine_pill"
            });
            Assert(result.Code == "machine_recipe_not_ready" && calls == 1, "machine refusal was lost or retried");
        });

        await Check("machine wait rejects malformed conditions and returns the final reading on timeout", async () =>
        {
            var registry = new ToolRegistry();
            int polls = 0;
            new MachineService(_ => null, (_, _, _, _) => null, (request, previousVessel) =>
            {
                Assert(request.ItemId == "machine" && request.ContainerId == "compartment" && request.Identifier == null,
                    "dry wait did not bind to the supplied machine compartment");
                Assert(previousVessel == (polls == 0 ? null : "vessel"), "subsequent polls did not retain the vessel ID");
                return new WaitReading { VesselId = "vessel", Data = new { samples = ++polls } };
            }).Register(registry);

            var args = MachineWaitArgs("output");
            args["identifier"] = "metal_ingot";
            args["quantity"] = 2147483648L;
            var rejected = await registry.ExecuteAsync("wait_for_machine", args, default);
            Assert(rejected.Code == "invalid_arguments" && polls == 0, "invalid wait reached the backend");

            var result = await registry.ExecuteAsync("wait_for_machine", MachineWaitArgs("dry"), default);
            Assert(result.Code == "machine_wait_timeout" && !result.Success && polls >= 2 &&
                (int)Doc(result)["data"]["samples"] == polls, "timeout did not return the last observation");
        });

        await Check("agent awaits machine output before applying the next production operation", async () =>
        {
            var registry = new ToolRegistry();
            int round = 0;
            int polls = 0;
            int operations = 0;
            new MachineService(_ => null, (_, _, _, _) =>
            {
                Assert(polls == 2, "operation ran before the output was observed");
                operations++;
                return HandsResult.Ok(null, "processed");
            }, (request, _) =>
            {
                Assert(request.Identifier == "metal_ingot" && request.Quantity == 3, "output condition changed");
                polls++;
                return new WaitReading { Ready = polls == 2, Data = new { quantity = polls == 2 ? 3 : 2 } };
            }).Register(registry);

            var args = MachineWaitArgs("output");
            args["timeout_seconds"] = 5;
            args["identifier"] = "metal_ingot";
            args["quantity"] = 3;
            var transport = new FakeTransport((messages, _) => ++round switch
            {
                1 => Call("wait", "wait_for_machine", args),
                2 => Call("next", "operate_machine", new JObject { ["item_id"] = "next-machine", ["action"] = "blend" }),
                _ => new ChatTurn { Text = "done" }
            });
            var result = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "生产", null, default);
            Assert(result.Reply == "done" && result.ToolCalls == 2 && operations == 1 &&
                result.Operations.Count == 1, "wait or operation history was incorrect");
        });

        await Check("cancelling machine wait stops the agent before later operations", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var registry = new ToolRegistry();
            int rounds = 0;
            int polls = 0;
            new MachineService(_ => null, (_, _, _, _) => throw new InvalidOperationException(), (_, _) =>
            {
                polls++;
                cancellation.Cancel();
                return new WaitReading { VesselId = "vessel" };
            }).Register(registry);
            var agent = new HandsAgent(new FakeTransport((_, _) =>
            {
                rounds++;
                return Call("wait", "wait_for_machine", MachineWaitArgs("dry"));
            }), registry);
            bool stopped = false;
            try { await agent.RunAsync(new ConnectionOptions(), "等待生产", null, cancellation.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Assert(stopped && polls == 1 && rounds == 1 && !agent.IsRunning, "wait swallowed cancellation or kept running");
        });

        await Check("a failed machine step skips later mutations in the same batch", async () =>
        {
            var registry = new ToolRegistry();
            int operations = 0;
            new MachineService(_ => null, (_, _, _, _) =>
            {
                operations++;
                return HandsResult.Ok(null);
            }, (_, _) => throw new HandsException("machine_vessel_changed", "vessel changed")).Register(registry);

            var first = Call("wait", "wait_for_machine", MachineWaitArgs("dry"));
            var second = Call("operate", "operate_machine", new JObject { ["item_id"] = "machine", ["action"] = "blend" });
            first.ToolCalls = new[] { first.ToolCalls[0], second.ToolCalls[0] };
            ((JArray)first.Message["tool_calls"]).Add(second.Message["tool_calls"][0].DeepClone());
            int round = 0;
            var transport = new FakeTransport((messages, _) =>
            {
                if (++round == 1) return first;
                var reply = JObject.Parse((string)messages.Last["content"]);
                Assert((string)reply["code"] == "not_executed", "model was not told to recheck before acting");
                return new ChatTurn { Text = "blocked" };
            });
            var result = await new HandsAgent(transport, registry).RunAsync(new ConnectionOptions(), "生产", null, default);
            Assert(result.Reply == "blocked" && operations == 0, "operation continued after failed prerequisite");
        });
    }

    private static JObject MachineWaitArgs(string condition) => new JObject
    {
        ["item_id"] = "machine", ["container_id"] = "compartment",
        ["condition"] = condition, ["timeout_seconds"] = 1
    };
}
