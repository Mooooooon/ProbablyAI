using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Macros
{
    internal sealed class MacroRunner
    {
        private readonly ToolRegistry registry;
        internal MacroRunner(ToolRegistry registry) => this.registry = registry;

        internal async Task<HandsResult> RunAsync(JObject definition, JObject inputs, CancellationToken cancellation,
            Action<string> progress = null)
        {
            MacroDefinition.Validate(definition, registry.IsGameplay);
            var values = new Dictionary<string, JToken> { ["inputs"] = MacroDefinition.BindInputs(definition, inputs) };
            string name = (string)definition["name"];
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            var run = new Execution(registry, deadline.Token, progress);
            try
            {
                await run.Steps((JArray)definition["steps"], "steps", values);
                deadline.Token.ThrowIfCancellationRequested();
                return run.Result(name, "ok", F("宏「{0}」执行完成。", name));
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return run.Result(name, cancellation.IsCancellationRequested ? "macro_cancelled" : "macro_timeout",
                    cancellation.IsCancellationRequested ? T("宏已停止。") : T("宏达到运行时间上限。"));
            }
            catch (HandsException error)
            {
                return run.Result(name, error.Code, error.Message);
            }
            catch (Exception)
            {
                return run.Result(name, "macro_error", T("宏执行未完成，请核对当前状态。"));
            }
        }

        private sealed class Execution
        {
            private readonly ToolRegistry registry;
            private readonly CancellationToken cancellation;
            private readonly Action<string> progress;
            private readonly Queue<string> operations = new Queue<string>();
            private readonly JArray reports = new JArray();
            private readonly JObject completedIterations = new JObject();
            private int calls;
            private int steps;
            private int mutations;
            private int omittedReports;
            private string path;
            private JToken failureData;

            internal Execution(ToolRegistry registry, CancellationToken cancellation, Action<string> progress)
            {
                this.registry = registry;
                this.cancellation = cancellation;
                this.progress = progress;
            }

            internal HandsResult Result(string name, string code, string message)
            {
                bool success = code == "ok";
                if (!success && path != null) message = F("宏「{0}」在 {1} 停止。{2}", name, path, message);
                if (completedIterations.Count == 1)
                    message += " " + F("已完成 {0} 轮。", completedIterations.First.First);
                if (!success && operations.Count > 0)
                    message += " " + F("最近的操作结果：{0}", operations.Last());
                if (reports.Last is JObject last)
                    message += " " + F("最近的记录：{0} = {1}", last["name"], last["value"].ToString(Formatting.None));
                return new HandsResult
                {
                    Success = success,
                    Code = code,
                    Message = message,
                    Data = new
                    {
                        macro_name = name, tool_calls = calls, verified_actions = mutations,
                        stopped_at = success ? null : path, completed_iterations = completedIterations,
                        reports, omitted_reports = omittedReports, recent_operations = operations.ToArray(),
                        failed_tool_data = failureData
                    }
                };
            }

            internal async Task Steps(JArray body, string prefix, Dictionary<string, JToken> parent)
            {
                var values = new Dictionary<string, JToken>(parent);
                for (int i = 0; i < body.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string at = prefix + "[" + (i + 1) + "]";
                    path = at;
                    if (++steps > 12000) throw new HandsException("macro_limit", T("宏达到步骤上限。"));
                    if (steps % 64 == 0)
                    {
                        await Task.Yield();
                        cancellation.ThrowIfCancellationRequested();
                    }
                    var step = (JObject)body[i];
                    switch ((string)step["op"])
                    {
                        case "call":
                            await Call(step, values);
                            break;
                        case "select":
                            values.Add((string)step["as"], Select(step, values));
                            break;
                        case "if":
                            string branch = Condition((JObject)step["test"], values) ? "then" : "else";
                            if (step[branch] is JArray nested) await Steps(nested, at + "." + branch, values);
                            break;
                        case "repeat":
                            int times = MacroDefinition.Integer(Value(step["times"], values), at);
                            if (times < 0 || times > MacroDefinition.MaxRepeat) throw MacroDefinition.Invalid(at + ".times");
                            // One entry per definition path, aggregating nested repetitions across outer iterations.
                            string key = System.Text.RegularExpressions.Regex.Replace(at, @"\(\d+\)", "");
                            completedIterations[key] ??= new JValue(0);
                            for (int iteration = 1; iteration <= times; iteration++)
                            {
                                var locals = new Dictionary<string, JToken>(values);
                                if ((string)step["as"] is string index) locals.Add(index, new JValue(iteration));
                                await Steps((JArray)step["steps"], at + "(" + iteration + ").steps", locals);
                                completedIterations[key] = (int)completedIterations[key] + 1;
                            }
                            break;
                        case "assert":
                            if (!Condition((JObject)step["test"], values))
                                throw new HandsException("macro_assertion", (string)step["message"]);
                            break;
                        case "report":
                            Report(step, values);
                            break;
                    }
                }
            }

            private async Task Call(JObject step, Dictionary<string, JToken> values)
            {
                string tool = (string)step["tool"];
                if (!registry.IsGameplay(tool))
                    throw new HandsException("macro_tool_unavailable", F("宏步骤 {0} 的工具 {1} 当前不可用。", path, tool));
                var arguments = new JObject(((JObject)step["args"]).Properties().Select(p => new JProperty(p.Name, Value(p.Value, values))));
                if (calls >= 600) throw new HandsException("macro_limit", T("宏达到工具调用上限。"));
                calls++;
                progress?.Invoke(F("正在运行宏步骤 {0}（{1}）…", path, tool));
                var result = await registry.ExecuteAsync(tool, arguments, cancellation);
                bool success = result.Success && result.Code != "partial";
                if (registry.ChangesState(tool))
                {
                    if (operations.Count == 5) operations.Dequeue();
                    operations.Enqueue(Clip(result.Message, 300));
                    if (success) mutations++;
                }
                if (!success)
                {
                    var resultData = SerializeData(result);
                    if (resultData.ToString(Formatting.None).Length <= 2500) failureData = resultData;
                    throw new HandsException(result.Code == "ok" || string.IsNullOrEmpty(result.Code) ? "macro_step_failed" : result.Code,
                        Clip(result.Message, 300));
                }
                var data = SerializeData(result);
                if (data is JObject obj && (IsTrue(obj["truncated"]) ||
                    (bool?)step["allow_paged"] != true && (HasMore(obj["page"]) || HasMore(obj["items"]))))
                    throw new HandsException("macro_incomplete_query", T("查询结果不完整，请缩小范围或显式处理分页。"));
                if ((string)step["as"] is string name) values.Add(name, data);
                // Yield between calls so the Stop button and native callbacks can run on Unity's main thread.
                await Task.Yield();
            }

            private static bool IsTrue(JToken token) => token?.Type == JTokenType.Boolean && (bool)token;
            private static bool HasMore(JToken token) => token is JObject page && IsTrue(page["has_more"]);

            private static JToken SerializeData(HandsResult result)
            {
                using var reader = new JsonTextReader(new System.IO.StringReader(HandsProtocol.Json(result.Data)))
                { DateParseHandling = DateParseHandling.None };
                return JToken.Load(reader);
            }

            private JToken Select(JObject step, Dictionary<string, JToken> values)
            {
                if (!(Value(step["from"], values) is JArray candidates))
                    throw MacroDefinition.Invalid(path + ".from");
                var locals = new Dictionary<string, JToken>(values);
                string mode = (string)step["mode"];
                JToken selected = null;
                foreach (var candidate in candidates)
                {
                    cancellation.ThrowIfCancellationRequested();
                    locals["item"] = candidate;
                    if (step["where"] is JObject filter && !Condition(filter, locals)) continue;
                    if (mode == "first") return candidate;
                    if (selected != null) throw new HandsException("macro_ambiguous_selection", T("匹配了多个目标，请缩小查询范围。"));
                    selected = candidate;
                }
                if (selected != null) return selected;
                if (mode == "optional_one") return JValue.CreateNull();
                throw new HandsException("macro_no_match", T("没有找到符合条件的目标或材料。"));
            }

            private void Report(JObject step, Dictionary<string, JToken> values)
            {
                var value = Value(step["value"], values);
                if (value.ToString(Formatting.None).Length > 300)
                    throw MacroDefinition.Invalid(path + ".value");
                if (reports.Count == 40) { reports.RemoveAt(0); omittedReports++; }
                reports.Add(new JObject { ["step"] = path, ["name"] = step["name"].DeepClone(), ["value"] = value });
            }

            private bool Condition(JObject test, Dictionary<string, JToken> values)
            {
                string op = (string)test["op"];
                switch (op)
                {
                    case "exists":
                        var value = Value(test["value"], values, allowMissing: true);
                        return value != null && value.Type != JTokenType.Null;
                    case "all": return ((JArray)test["tests"]).All(part => Condition((JObject)part, values));
                    case "any": return ((JArray)test["tests"]).Any(part => Condition((JObject)part, values));
                    case "not": return !Condition((JObject)test["test"], values);
                }
                var left = Value(test["left"], values);
                var right = Value(test["right"], values);
                bool numbers = IsNumber(left) && IsNumber(right);
                if (left.Type == JTokenType.Null || right.Type == JTokenType.Null)
                    throw new HandsException("macro_missing_value", F("宏条件缺少可比较的值，位置 {0}。", path));
                if (op == "eq" || op == "ne")
                {
                    bool same = numbers ? (decimal)left == (decimal)right : JToken.DeepEquals(left, right);
                    return op == "eq" ? same : !same;
                }
                if (!numbers) throw MacroDefinition.Invalid(path + ".test");
                int comparison = decimal.Compare((decimal)left, (decimal)right);
                return op switch { "lt" => comparison < 0, "le" => comparison <= 0, "gt" => comparison > 0, _ => comparison >= 0 };
            }

            private JToken Value(JToken token, Dictionary<string, JToken> values, bool allowMissing = false)
            {
                if (token is JObject obj)
                {
                    if (obj.Count == 1 && (string)obj["ref"] is string reference)
                    {
                        string[] parts = reference.Split('.');
                        values.TryGetValue(parts[0], out var current);
                        foreach (string part in parts.Skip(1))
                            current = (current as JObject)?[part];
                        if (current == null || current.Type == JTokenType.Null)
                        {
                            if (allowMissing) return null;
                            throw new HandsException("macro_missing_value", F("宏引用 {0} 的值缺失。", reference));
                        }
                        return current.DeepClone();
                    }
                    if (obj.Count == 1 && (obj["add"] ?? obj["multiply"]) is JArray pair)
                    {
                        int left = MacroDefinition.Integer(Value(pair[0], values), path);
                        int right = MacroDefinition.Integer(Value(pair[1], values), path);
                        long number = obj["add"] != null ? (long)left + right : (long)left * right;
                        if (number < int.MinValue || number > int.MaxValue) throw MacroDefinition.Invalid(path);
                        return new JValue((int)number);
                    }
                    return new JObject(obj.Properties().Select(p => new JProperty(p.Name, Value(p.Value, values))));
                }
                if (token is JArray array) return new JArray(array.Select(element => Value(element, values)));
                return token.DeepClone();
            }

            private static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
            private static string Clip(string message, int max)
                => message != null && message.Length > max ? message.Substring(0, max) + "…" : message ?? "";
        }
    }
}
