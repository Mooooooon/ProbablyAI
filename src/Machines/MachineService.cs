using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using ProbablyAI.Tools;

namespace ProbablyAI.Machines
{
    internal sealed class MachineService
    {
        internal const string InspectDescription = "查询自有机器当前状态、分区 ID 和可用的生产控制。先用 find_items 找机器物品 ID（固定的机器需 include_pinned=true）。自动设备按原生条件运行。投料、供电、装模组和转运使用 move_item 或 move_items。";
        internal const string OperateTool = "operate_machine";
        internal const string WaitTool = "wait_for_machine";
        internal const string OperateDescription = "对自有机器执行一次 inspect_machine 返回的 action。按控制选项传 value，先备料、供电并留出成品空间。屠宰控制只用于主人明确要求的屠宰加工。水瓶打印请用 print_bottle。读到操作结果后再发起依赖它的操作。";
        internal const string WaitDescription = "等机器达到条件再继续生产链。output 等指定成品分区内指定 identifier 的自有物品总数达到 quantity（含已有物品，先查询基线）。dry 等指定容器分区内同一个容器的内容物达到游戏干燥标准且仍有内容物。每次最多等 60 秒。先读取等待结果，再发起依赖该结果的操作。";
        internal const string ItemIdDescription = "机器的实际物品 ID，来自物品查询。";
        internal const string ActionDescription = "inspect_machine 返回的控制 action。";
        internal const string ValueDescription = "控制要求的参数。配方 ID 为字符串，饲料目标百分比为整数，其余控制省略。";
        internal const string ContainerDescription = "inspect_machine 返回的目标分区 ID。";
        internal const string IdentifierDescription = "成品的游戏 identifier，用于 output 条件。";
        internal const string QuantityDescription = "output 条件的目标总数量，包含分区内已有的成品。";
        internal const string TimeoutDescription = "最长等待秒数，默认 15，允许 1–60。";

        private readonly Func<string, HandsResult> inspect;
        private readonly Func<string, string, string, int?, HandsResult> operate;
        private readonly Func<MachineWait, string, WaitReading> observe;

        public MachineService(Func<string, HandsResult> inspect,
            Func<string, string, string, int?, HandsResult> operate,
            Func<MachineWait, string, WaitReading> observe)
        {
            this.inspect = inspect;
            this.operate = operate;
            this.observe = observe;
        }

        public void Register(ToolRegistry registry)
        {
            registry.Register(ItemUseProtocol.Tool("inspect_machine", InspectDescription,
                new JObject { ["item_id"] = Field("string", ItemIdDescription) }, "item_id"), Inspect);
            registry.Register(ItemUseProtocol.Tool(OperateTool, OperateDescription, new JObject
            {
                ["item_id"] = Field("string", ItemIdDescription),
                ["action"] = Field("string", ActionDescription),
                ["value"] = new JObject { ["type"] = new JArray("string", "integer"), ["description"] = ValueDescription }
            }, "item_id", "action"), Operate, changesState: true);
            registry.RegisterAsync(ItemUseProtocol.Tool(WaitTool, WaitDescription, new JObject
            {
                ["item_id"] = Field("string", ItemIdDescription),
                ["container_id"] = Field("string", ContainerDescription),
                ["condition"] = new JObject { ["type"] = "string", ["enum"] = new JArray("output", "dry") },
                ["identifier"] = Field("string", IdentifierDescription),
                ["quantity"] = Integer(1, int.MaxValue, QuantityDescription),
                ["timeout_seconds"] = Integer(1, 60, TimeoutDescription)
            }, "item_id", "container_id", "condition"), WaitAsync);
        }

        private HandsResult Inspect(JObject args)
            => ReadText(args["item_id"]) is string id && HasOnly(args, "item_id") ? inspect(id) : Invalid();

        private HandsResult Operate(JObject args)
        {
            var id = ReadText(args["item_id"]);
            var action = ReadText(args["action"]);
            var value = args["value"];
            if (id == null || action == null || !HasOnly(args, "item_id", "action", "value")) return Invalid();
            var recipe = ReadText(value);
            var target = ReadInt(value, 0, 100);
            if (action == "set_recipe" ? recipe == null :
                action == "set_feed_target" ? !target.HasValue || target.Value % 5 != 0 : value != null)
                return Invalid();
            return operate(id, action, recipe, target);
        }

        private async Task<HandsResult> WaitAsync(JObject args, CancellationToken cancellationToken)
        {
            var id = ReadText(args["item_id"]);
            var container = ReadText(args["container_id"]);
            var condition = ReadText(args["condition"]);
            var identifier = ReadText(args["identifier"]);
            var quantity = ReadInt(args["quantity"], 1, int.MaxValue);
            var timeout = args["timeout_seconds"] == null ? 15 : ReadInt(args["timeout_seconds"], 1, 60);
            if (id == null || container == null || !timeout.HasValue ||
                !HasOnly(args, "item_id", "container_id", "condition", "identifier", "quantity", "timeout_seconds") ||
                (condition == "output" ? identifier == null || !quantity.HasValue :
                    condition != "dry" || args["identifier"] != null || args["quantity"] != null))
                return Invalid();

            var request = new MachineWait(id, container, identifier, quantity.GetValueOrDefault());
            var elapsed = Stopwatch.StartNew();
            string vesselId = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reading = observe(request, vesselId);
                vesselId ??= reading.VesselId;
                if (reading.Ready) return HandsResult.Ok(reading.Data, "已达到机器等待条件。");
                var remaining = TimeSpan.FromSeconds(timeout.Value) - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    var result = HandsResult.Fail("machine_wait_timeout", "等待时间已到，机器尚未达到条件。");
                    result.Data = reading.Data;
                    return result;
                }
                await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        private static JObject Field(string type, string description)
            => new JObject { ["type"] = type, ["description"] = description };

        private static JObject Integer(int min, int max, string description)
            => new JObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["description"] = description };

        private static bool HasOnly(JObject args, params string[] fields)
            => args.Properties().All(property => fields.Contains(property.Name));

        private static string ReadText(JToken token)
        {
            var value = token?.Type == JTokenType.String ? ((string)token).Trim() : null;
            return !string.IsNullOrEmpty(value) && value.Length <= 200 ? value : null;
        }

        private static int? ReadInt(JToken token, int min, int max)
            => token?.Type == JTokenType.Integer && int.TryParse(token.ToString(), out int value) &&
                value >= min && value <= max ? value : (int?)null;

        private static HandsResult Invalid()
            => HandsResult.Fail("invalid_arguments", "机器工具参数无效，请检查分区、控制和参数值。");
    }

    internal sealed class MachineWait
    {
        public string ItemId { get; }
        public string ContainerId { get; }
        public string Identifier { get; }
        public int Quantity { get; }

        public MachineWait(string itemId, string containerId, string identifier, int quantity)
        {
            ItemId = itemId;
            ContainerId = containerId;
            Identifier = identifier;
            Quantity = quantity;
        }
    }

    internal sealed class WaitReading
    {
        public bool Ready { get; set; }
        public string VesselId { get; set; }
        public object Data { get; set; }
    }
}
