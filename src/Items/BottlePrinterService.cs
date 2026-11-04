using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Items
{
    internal enum BottleSize { Small, Normal, Large }

    internal sealed class BottlePrinterService
    {
        private const string ToolName = "print_bottle";
        internal const string Description = "用玩家拥有的水瓶打印机（bottle_printer）打印一个空水瓶。先用 find_items 搜索 bottle_printer（固定的机器需 include_pinned=true），再传入实际物品 ID 和规格 small、normal 或 large。游戏决定规格是否已解锁、电量和成品区容量。瓶子留在打印机成品区，返回实际物品和容器 ID。";
        internal const string PrinterIdDescription = "水瓶打印机的实际物品 ID，来自物品查询。";
        internal const string SizeDescription = "空水瓶规格：small 为小号，normal 为普通，large 为大号。";
        private readonly Func<string, BottleSize, ItemInfo> print;

        public BottlePrinterService(Func<string, BottleSize, ItemInfo> print) => this.print = print;

        public void Register(ToolRegistry registry)
        {
            var properties = new JObject
            {
                ["item_id"] = new JObject { ["type"] = "string", ["description"] = PrinterIdDescription },
                ["size"] = new JObject
                {
                    ["type"] = "string", ["enum"] = new JArray("small", "normal", "large"),
                    ["description"] = SizeDescription
                }
            };
            registry.Register(ItemUseProtocol.Tool(ToolName, Description, properties, "item_id", "size"), Execute, changesState: true);
        }

        private HandsResult Execute(JObject arguments)
        {
            string itemId = arguments["item_id"]?.Type == JTokenType.String ? (string)arguments["item_id"] : null;
            var sizeToken = arguments["size"];
            if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 200 || sizeToken?.Type != JTokenType.String ||
                arguments.Properties().Any(property => property.Name != "item_id" && property.Name != "size"))
                return InvalidArguments();

            BottleSize size;
            switch ((string)sizeToken)
            {
                case "small": size = BottleSize.Small; break;
                case "normal": size = BottleSize.Normal; break;
                case "large": size = BottleSize.Large; break;
                default: return InvalidArguments();
            }

            var bottle = print(itemId.Trim(), size);
            return HandsResult.Ok(new
            {
                item_id = bottle.Id,
                identifier = bottle.Identifier,
                name = bottle.Name,
                container_id = bottle.ContainerId,
                path = bottle.Path,
                quantity = 1
            }, F("已打印一个空水瓶（{0}），放在打印机成品区。", bottle.Name));
        }

        private static HandsResult InvalidArguments()
            => HandsResult.Fail("invalid_arguments", T("请提供水瓶打印机物品 ID 和规格 small、normal 或 large。"));
    }
}
