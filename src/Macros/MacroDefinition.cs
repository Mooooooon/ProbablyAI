using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Macros
{
    internal static class MacroDefinition
    {
        internal const int MaxCharacters = 32000;
        internal const int MaxEditorCharacters = 128000;
        internal const int MaxRepeat = 20;
        private static readonly Regex Symbol = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$");
        private static readonly Regex SessionId = new Regex(@"^[ic]-[0-9a-fA-F]{8}-[0-9]+$");

        internal static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxEditorCharacters)
                throw Invalid("definition");
            using var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            var definition = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw Invalid("definition");
            return definition;
        }

        internal static void Validate(JObject definition, Func<string, bool> hasTool = null)
        {
            Fields(definition, "definition", "version name steps", "inputs");
            if (definition.ToString(Formatting.None).Length > MaxCharacters ||
                definition["version"]?.Type != JTokenType.Integer || (string)definition["version"] != "1")
                throw Invalid("version");
            String(definition["name"], "name", 60);
            if (definition["inputs"] != null)
            {
                if (!(definition["inputs"] is JObject inputs) || inputs.Count > 12) throw Invalid("inputs");
                foreach (var input in inputs.Properties())
                {
                    string path = "inputs." + input.Name;
                    if (!Symbol.IsMatch(input.Name) || !(input.Value is JObject field)) throw Invalid(path);
                    Fields(field, path, "type", "default min max");
                    string type = String(field["type"], path);
                    if (type != "integer" && type != "boolean" && type != "string") throw Invalid(path);
                    if (type == "integer")
                    {
                        if (Limit(field, "min", int.MinValue, path) > Limit(field, "max", int.MaxValue, path))
                            throw Invalid(path);
                    }
                    else if (field["min"] != null || field["max"] != null) throw Invalid(path);
                    if (field["default"] != null) ValidateInput(field, field["default"], path);
                    if (field["default"]?.Type == JTokenType.String && SessionId.IsMatch((string)field["default"]))
                        throw Invalid(path);
                }
            }
            int count = 0;
            ValidateSteps(definition["steps"], "steps", 0, ref count, hasTool, new HashSet<string> { "inputs" });
        }

        internal static JObject BindInputs(JObject definition, JObject supplied)
        {
            supplied ??= new JObject();
            var declared = definition["inputs"] as JObject ?? new JObject();
            foreach (var field in supplied.Properties())
                if (declared[field.Name] == null) throw Invalid("inputs." + field.Name);
            var result = new JObject();
            foreach (var field in declared.Properties())
            {
                var spec = (JObject)field.Value;
                var value = supplied[field.Name] ?? spec["default"];
                ValidateInput(spec, value, "inputs." + field.Name);
                result.Add(field.Name, value.DeepClone());
            }
            return result;
        }

        private static void ValidateInput(JObject spec, JToken value, string path)
        {
            switch ((string)spec["type"])
            {
                case "integer":
                    int number = Integer(value, path);
                    if (number < Limit(spec, "min", int.MinValue, path) || number > Limit(spec, "max", int.MaxValue, path))
                        throw Invalid(path);
                    break;
                case "boolean":
                    if (value?.Type != JTokenType.Boolean) throw Invalid(path);
                    break;
                case "string":
                    if (value?.Type != JTokenType.String || ((string)value).Length > 200) throw Invalid(path);
                    break;
            }
        }

        private static int Limit(JObject spec, string name, int fallback, string path)
            => spec[name] == null ? fallback : Integer(spec[name], path + "." + name);

        internal static int Integer(JToken token, string path)
        {
            if (token?.Type == JTokenType.Integer && int.TryParse((string)token, out int value)) return value;
            throw Invalid(path);
        }

        private static void ValidateSteps(JToken token, string path, int depth, ref int count,
            Func<string, bool> hasTool, HashSet<string> inherited)
        {
            if (!(token is JArray steps) || steps.Count == 0 || depth > 6) throw Invalid(path);
            var names = new HashSet<string>(inherited);
            for (int i = 0; i < steps.Count; i++)
            {
                string at = path + "[" + (i + 1) + "]";
                if (++count > 160 || !(steps[i] is JObject step)) throw Invalid(at);
                switch (String(step["op"], at))
                {
                    case "call":
                        Fields(step, at, "op tool args", "as allow_paged");
                        string tool = String(step["tool"], at, 64);
                        if (hasTool != null && !hasTool(tool))
                            throw new HandsException("macro_tool_unavailable", F("宏步骤 {0} 的工具 {1} 当前不可用。", at, tool));
                        if (!(step["args"] is JObject args) ||
                            step["allow_paged"] != null && step["allow_paged"].Type != JTokenType.Boolean)
                            throw Invalid(at);
                        foreach (var argument in args.Properties()) Value(argument.Value, names, at);
                        BindName(step["as"], names, at);
                        break;
                    case "select":
                        Fields(step, at, "op from mode as", "where");
                        string mode = String(step["mode"], at);
                        if (mode != "one" && mode != "optional_one" && mode != "first") throw Invalid(at);
                        Value(step["from"], names, at);
                        if (step["where"] != null)
                            Condition(step["where"], new HashSet<string>(names) { "item" }, at);
                        BindName(step["as"], names, at);
                        break;
                    case "if":
                        Fields(step, at, "op test then", "else");
                        Condition(step["test"], names, at);
                        ValidateSteps(step["then"], at + ".then", depth + 1, ref count, hasTool, names);
                        if (step["else"] != null) ValidateSteps(step["else"], at + ".else", depth + 1, ref count, hasTool, names);
                        break;
                    case "repeat":
                        Fields(step, at, "op times steps", "as");
                        Value(step["times"], names, at);
                        var bodyNames = new HashSet<string>(names);
                        BindName(step["as"], bodyNames, at);
                        ValidateSteps(step["steps"], at + ".steps", depth + 1, ref count, hasTool, bodyNames);
                        break;
                    case "assert":
                        Fields(step, at, "op test message");
                        Condition(step["test"], names, at);
                        String(step["message"], at, 200);
                        break;
                    case "report":
                        Fields(step, at, "op name value");
                        String(step["name"], at, 60);
                        Value(step["value"], names, at);
                        break;
                    default:
                        throw Invalid(at);
                }
            }
        }

        private static void BindName(JToken name, HashSet<string> names, string path)
        {
            if (name == null) return;
            string symbol = String(name, path);
            if (!Symbol.IsMatch(symbol) || symbol == "item" || !names.Add(symbol)) throw Invalid(path + ".as");
        }

        private static void Condition(JToken token, HashSet<string> names, string path, int depth = 0)
        {
            if (depth > 8 || !(token is JObject test)) throw Invalid(path);
            switch (String(test["op"], path))
            {
                case "all":
                case "any":
                    Fields(test, path, "op tests");
                    if (!(test["tests"] is JArray tests) || tests.Count == 0 || tests.Count > 16) throw Invalid(path);
                    foreach (var part in tests) Condition(part, names, path, depth + 1);
                    break;
                case "not":
                    Fields(test, path, "op test");
                    Condition(test["test"], names, path, depth + 1);
                    break;
                case "exists":
                    Fields(test, path, "op value");
                    Value(test["value"], names, path);
                    break;
                case "eq": case "ne": case "lt": case "le": case "gt": case "ge":
                    Fields(test, path, "op left right");
                    Value(test["left"], names, path);
                    Value(test["right"], names, path);
                    break;
                default:
                    throw Invalid(path);
            }
        }

        private static void Value(JToken token, HashSet<string> names, string path, int depth = 0)
        {
            if (token == null || depth > 12) throw Invalid(path);
            if (token is JObject obj)
            {
                if (obj.Count == 1 && obj["ref"] != null)
                {
                    string reference = String(obj["ref"], path, 160);
                    string[] fields = reference.Split('.');
                    if (!names.Contains(fields[0]) || fields.Any(string.IsNullOrWhiteSpace)) throw Invalid(path);
                    return;
                }
                // Integer arithmetic lets repeat indices drive tool pagination.
                if (obj.Count == 1 && (obj["add"] ?? obj["multiply"]) is JToken operands)
                {
                    if (!(operands is JArray pair) || pair.Count != 2) throw Invalid(path);
                    foreach (var part in pair) Value(part, names, path, depth + 1);
                    return;
                }
                foreach (var field in obj.Properties()) Value(field.Value, names, path, depth + 1);
            }
            else if (token is JArray array)
            {
                foreach (var element in array) Value(element, names, path, depth + 1);
            }
            else if (token.Type == JTokenType.String)
            {
                if (SessionId.IsMatch((string)token)) throw Invalid(path);
            }
            else if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float &&
                     token.Type != JTokenType.Boolean && token.Type != JTokenType.Null) throw Invalid(path);
        }

        internal static string String(JToken token, string path, int limit = 200)
        {
            if (token?.Type == JTokenType.String && (string)token is string value &&
                !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl)) return value;
            throw Invalid(path);
        }

        internal static void Fields(JObject obj, string path, string required, string optional = "")
        {
            var must = required.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var allowed = must.Concat(optional.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (obj == null || must.Any(key => !obj.ContainsKey(key)) ||
                obj.Properties().Any(field => !allowed.Contains(field.Name))) throw Invalid(path);
        }

        internal static HandsException Invalid(string path)
            => new HandsException("invalid_macro", F("宏定义或输入无效，位置 {0}。", path));
    }
}
