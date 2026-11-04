using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Items;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Macros
{
    internal sealed class MacroService
    {
        internal const string RunTool = "run_macro";
        internal const string SaveDraftTool = "save_macro_draft";
        internal const string ListDescription = "列出宏、批准状态、版本和输入。玩家要求运行已保存的宏时，按 ID 调用 run_macro。";
        internal const string GetDescription = "读取宏的批准版和待审草稿。省略 macro_id 可查编写语法和当前可用的游戏工具。";
        internal const string SaveDescription = "创建或更新待玩家审核的宏草稿。更新时必须提供列表或查询返回的 macro_id 和 revision。仅按玩家要求编写或修改。";
        internal const string RunDescription = "玩家要求时，按 ID 和输入运行已批准的宏。使用当前游戏状态。失败或部分完成后先报告停止位置和实际结果，再等玩家指示。";
        internal const string Guide = "定义为 {version:1,name,inputs?,steps}。inputs 按名称定义 {type:integer|boolean|string,default?,min?,max?}，无默认值则必填。每步以 op 指定类型。call: {tool,args,as?,allow_paged?}，as 绑定结果 data。select: {from,mode:one|optional_one|first,as,where?}，where 用 item 引用候选对象。if: {test,then,else?}。repeat: {times,steps,as?}，as 为从 1 开始的轮次。assert: {test,message}。report: {name,value}。值可写 {ref:\"inputs.name\"} 或 {ref:\"变量.字段\"}，整数运算为 {add:[a,b]} 或 {multiply:[a,b]}。条件为 {op:exists,value}、{op:eq|ne|lt|le|gt|ge,left,right}、{op:all|any,tests} 或 {op:not,test}。每个分支和每轮使用局部变量。通过当前查询取得物品和分区 ID，勿写死会话 ID。";
        private readonly ToolRegistry registry;
        private const string EnglishGuide = "Definition: {version:1,name,inputs?,steps}. Each input is named and has {type:integer|boolean|string,default?,min?,max?}. Inputs without defaults are required. Each step has op. call: {tool,args,as?,allow_paged?}, with as binding the result data. select: {from,mode:one|optional_one|first,as,where?}, with item referring to each candidate. if: {test,then,else?}. repeat: {times,steps,as?}, with as binding a one-based iteration. assert: {test,message}. report: {name,value}. Reference values with {ref:\"inputs.name\"} or {ref:\"variable.field\"}. Integer arithmetic uses {add:[a,b]} or {multiply:[a,b]}. Conditions use {op:exists,value}, {op:eq|ne|lt|le|gt|ge,left,right}, {op:all|any,tests}, or {op:not,test}. Branch and iteration variables are local. Query current item and compartment IDs during execution.";
        private readonly MacroRunner runner;
        internal MacroStore Store { get; }

        internal MacroService(MacroStore store, ToolRegistry registry)
        {
            Store = store;
            this.registry = registry;
            runner = new MacroRunner(registry);
        }

        internal void Register()
        {
            registry.RegisterOrchestration(ItemUseProtocol.Tool("list_macros", ListDescription, new JObject()), args =>
            {
                MacroDefinition.Fields(args, "arguments", "");
                return HandsResult.Ok(new
                {
                    macros = Store.List().Select(entry => new
                    {
                        macro_id = entry.Id, name = entry.Name, revision = entry.Revision,
                        approved = entry.Approved != null, has_draft = entry.Draft != null,
                        inputs = (entry.Approved ?? entry.Draft)["inputs"] as JObject ?? new JObject()
                    }).ToArray(),
                    warning = Store.Warning
                });
            });
            registry.RegisterOrchestration(ItemUseProtocol.Tool("get_macro", GetDescription,
                new JObject { ["macro_id"] = Type("string") }), args =>
            {
                MacroDefinition.Fields(args, "arguments", "", "macro_id");
                if (args["macro_id"] == null) return HandsResult.Ok(new { guide = IsEnglish ? EnglishGuide : Guide, tools = registry.GameplayDefinitions() });
                var entry = Store.Get(Id(args));
                return HandsResult.Ok(new { macro_id = entry.Id, revision = entry.Revision, approved = entry.Approved, draft = entry.Draft });
            });
            registry.RegisterOrchestration(ItemUseProtocol.Tool(SaveDraftTool, SaveDescription,
                new JObject { ["macro_id"] = Type("string"), ["revision"] = Type("integer"), ["definition"] = Type("object") },
                "definition"), args =>
            {
                MacroDefinition.Fields(args, "arguments", "definition", "macro_id revision");
                if (!(args["definition"] is JObject definition)) throw MacroDefinition.Invalid("definition");
                long? revision = null;
                if (args["revision"] != null)
                {
                    if (!MacroStore.TryRevision(args["revision"], out long expected)) throw MacroDefinition.Invalid("revision");
                    revision = expected;
                }
                var entry = Save(args["macro_id"] == null ? null : Id(args), revision, definition, draft: true);
                return HandsResult.Ok(new { macro_id = entry.Id, revision = entry.Revision, name = (string)entry.Draft["name"], pending_approval = true },
                    T("草稿已保存，请在聊天面板审核。"));
            });
            registry.RegisterAsync(ItemUseProtocol.Tool(RunTool, RunDescription,
                new JObject { ["macro_id"] = Type("string"), ["inputs"] = Type("object") }, "macro_id"),
                (args, token) =>
                {
                    MacroDefinition.Fields(args, "arguments", "macro_id", "inputs");
                    if (args["inputs"] != null && !(args["inputs"] is JObject)) throw MacroDefinition.Invalid("inputs");
                    return RunAsync(Id(args), args["inputs"] as JObject, token);
                }, changesState: true, orchestration: true);
        }

        internal MacroStore.Entry Save(string id, long? revision, JObject definition, bool draft)
        {
            Validate(definition);
            return Store.Save(id, revision, definition, draft);
        }

        internal void Approve(string id, long revision) => Store.Approve(id, revision, Validate);

        internal void Validate(JObject definition) => MacroDefinition.Validate(definition, registry.IsGameplay);

        internal Task<HandsResult> RunAsync(string id, JObject inputs, CancellationToken token, Action<string> progress = null,
            long? revision = null)
        {
            var entry = Store.Get(id);
            if (entry.Approved == null) throw new HandsException("macro_not_approved", T("宏需要玩家批准后才能运行。"));
            if (revision.HasValue && revision != entry.Revision)
                throw new HandsException("macro_revision_conflict", T("宏已有新版本，请重新载入后再编辑。"));
            return runner.RunAsync(entry.Approved, inputs, token, progress);
        }

        private static string Id(JObject args) => MacroDefinition.String(args["macro_id"], "macro_id", 32);
        private static JObject Type(string type) => new JObject { ["type"] = type };
    }
}
