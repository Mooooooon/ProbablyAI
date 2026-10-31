using Newtonsoft.Json.Linq;

namespace ProbablyAI.Documents
{
    public static class DocumentProtocol
    {
        public static JArray Tools() => new JArray
        {
            Tool("read_document", "只读读取真实文档的文字：导师笔记/情报单、带 BOOK_ID 的书籍，以及香烟 / 食品券 / 注射器 / 机械手等指南和通缉令的界面文字。返回 kind、title、body、signature、pages 或 text。主人询问笔记、书籍或指南内容时，先用 find_items / list_container 找到物品，再调用本工具；inspect_item 的 description 只是简介，不能当作全文，也不能根据物品名称编造内容。只读，不打开窗口、不翻页、不改变物品；无法阅读时返回原因。")
        };

        private static JObject Tool(string name, string description) => new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = name, ["description"] = description,
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["item_id"] = new JObject { ["type"] = "string", ["description"] = "find_items / list_container 返回的真实物品 ID。" }
                    },
                    ["required"] = new JArray("item_id"), ["additionalProperties"] = false
                }
            }
        };
    }
}
