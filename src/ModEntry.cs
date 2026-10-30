using UnityEngine;
using ProbablyAI.Tools;

namespace ProbablyAI
{
    public sealed class ModEntry : IMod
    {
        public const string ModId = "probably_ai";
        private static AssistantController runtime;
        public static ToolRegistry Tools => runtime?.Tools;
        public static event System.Action<ToolRegistry> ToolsReady;
        private AssistantSettings settings;
        private ModLog log;
        private GameObject host;
        private Services.OpenAiApiClient api;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            settings = new AssistantSettings(ModHelper.GetConfig(ModId));
        }

        public void OnEnable()
        {
            if (host != null) return;
            host = new GameObject("ProbablyAI.Runtime");
            Object.DontDestroyOnLoad(host);
            var connection = new Services.ConnectionSettingsStore(
                () => ModHelper.ReadModDataFileText(ModId, "connection.local.json"),
                text => ModHelper.WriteModDataFileText(ModId, "connection.local.json", text));
            api = new Services.OpenAiApiClient();
            runtime = host.AddComponent<AssistantController>();
            runtime.Initialize(settings, log, connection, new Services.OpenAiAssistantService(api), api);
            log.Log(Branding.Name + " v" + Branding.Version + " enabled. Click the portrait to chat; F7 opens settings.");
        }

        public void OnDisable()
        {
            if (host == null) return;
            host.GetComponent<AssistantController>().Shutdown();
            Object.Destroy(host);
            host = null;
            runtime = null;
            api?.Dispose();
            api = null;
        }

        internal static void NotifyToolsReady(ToolRegistry tools)
        {
            if (ToolsReady == null) return;
            foreach (System.Action<ToolRegistry> register in ToolsReady.GetInvocationList())
                try { register(tools); }
                catch (System.Exception) { Debug.LogWarning("[" + Branding.Name + "] 扩展工具注册未完成。"); }
        }
    }
}
