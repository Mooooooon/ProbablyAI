using UnityEngine;
using ProbablyAI.Tools;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI
{
    public sealed class ModEntry : IMod
    {
        public const string ModId = "probably_ai";
        private static ModManifest activeManifest;
        private static string originalDescription;
        private static AssistantSettings activeSettings;
        private static AssistantController runtime;
        public static ToolRegistry Tools => runtime?.Tools;
        public static event System.Action<ToolRegistry> ToolsReady;
        private AssistantSettings settings;
        private ModLog log;
        private GameObject host;
        private Services.OpenAiApiClient api;

        public void Init(ModManifest manifest)
        {
            activeManifest = manifest;
            originalDescription = manifest.Description;
            RefreshLanguage();
            log = new ModLog(manifest);
            settings = new AssistantSettings(ModHelper.GetConfig(ModId));
            activeSettings = settings;
        }

        internal static bool RefreshLanguage()
        {
            bool changed = ProbablyAI.Localization.Text.UseLocale(LocHelper.GetCurrentLocaleCode());
            if (changed) activeSettings?.RefreshLanguage();
            if (activeManifest != null)
            {
                activeManifest.Name = Branding.DisplayName;
                activeManifest.Description = T(originalDescription);
            }
            return changed;
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
                catch (System.Exception) { Debug.LogWarning("[" + Branding.Name + T("] 扩展工具注册未完成。")); }
        }
    }
}
