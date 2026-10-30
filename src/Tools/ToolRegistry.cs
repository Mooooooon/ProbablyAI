using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Tools
{
    public sealed class ToolRegistry
    {
        private sealed class Entry
        {
            public JObject Definition;
            public Func<JObject, HandsResult> Execute;
            public bool ChangesState;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public void Register(JObject definition, Func<JObject, HandsResult> execute, bool changesState = false)
        {
            var name = (string)definition?["function"]?["name"];
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, "^[a-zA-Z0-9_-]{1,64}$") ||
                (string)definition["type"] != "function" || !(definition["function"]?["parameters"] is JObject) || execute == null)
                throw new ArgumentException(T("工具必须提供有效的 function 名称、参数 Schema 和处理函数。"));
            if (entries.ContainsKey(name)) throw new ArgumentException(T("工具名称重复：") + name);
            entries.Add(name, new Entry { Definition = (JObject)definition.DeepClone(), Execute = execute, ChangesState = changesState });
        }

        public JArray Definitions()
        {
            var definitions = new JArray(entries.Values.Select(e => e.Definition.DeepClone()));
            TranslateToolDescriptions(definitions);
            return definitions;
        }
        public bool ChangesState(string name) => name != null && entries.TryGetValue(name, out var entry) && entry.ChangesState;

        public HandsResult Execute(string name, JObject arguments)
        {
            if (name == null || !entries.TryGetValue(name, out var entry)) return HandsResult.Fail("unknown_tool", T("这个工具当前不可用。"));
            if (arguments == null) return HandsResult.Fail("invalid_arguments", T("工具参数必须是 JSON 对象。"));
            try { return entry.Execute(arguments) ?? HandsResult.Fail("empty_tool_result", T("工具没有返回结果，请检查当前状态。")); }
            catch (HandsException error) { return HandsResult.Fail(error.Code, error.Message); }
            catch (Exception) { return HandsResult.Fail("tool_error", T("工具执行未完成，请检查当前状态后继续。")); }
        }

        public static ToolRegistry ForHands(HandsService hands)
        {
            var registry = new ToolRegistry();
            foreach (JObject definition in HandsProtocol.Tools())
            {
                string name = (string)definition["function"]["name"];
                registry.Register(definition, arguments => hands.Execute(name, arguments),
                    name == "move_item" || name == "open_container" || name == "close_container");
            }
            return registry;
        }
    }
}
