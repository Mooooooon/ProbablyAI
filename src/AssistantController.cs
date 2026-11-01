using System.Collections;
using ProbablyAI.Services;
using ProbablyAI.UI;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using ProbablyAI.Customers;
using ProbablyAI.Documents;
using ProbablyAI.Inspection;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace ProbablyAI
{
    public sealed class AssistantController : MonoBehaviour
    {
        private AssistantSettings settings;
        private ModLog log;
        private IAssistantService service;
        private ConnectionSettingsStore connection;
        private PortraitView portrait;
        private ConnectionWindow panel;
        private HandsWindow handsPanel;
        private GameHandsBackend handsBackend;
        private GameCustomerBackend customerBackend;
        private HandsService hands;
        private HandsAgent agent;
        private IChatTransport transport;
        private ChatWindow chatPanel;
        private ConversationMemory conversation;
        public ToolRegistry Tools { get; private set; }
        private bool subscribed;
        private bool languageSubscribed;
        private Coroutine languageRoutine;
        private bool gameReady;
        private bool sessionHidden;
        private float nextBuildAttempt;
        private float nextVisitCheck;
        private string notifiedVisitId;
        private VisitNoticeView visitNotice;

        public void Initialize(AssistantSettings settings, ModLog log, ConnectionSettingsStore connection, IAssistantService service, IChatTransport transport)
        {
            this.settings = settings;
            this.log = log;
            this.service = service;
            this.connection = connection;
            this.transport = transport;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
            languageRoutine = StartCoroutine(RefreshLanguageWhenReady());
        }

        private IEnumerator RefreshLanguageWhenReady()
        {
            // ModLoader calls Init/OnEnable during Awake. MainMenuUIController.Start
            // starts LocalizationSettings.InitializationOperation later, so wait one
            // frame to let every Start() run before touching the global locale state.
            yield return null;

            float deadline = Time.realtimeSinceStartup + 15f;
            while (!LocalizationSettings.HasSettings && Time.realtimeSinceStartup < deadline)
                yield return new WaitForSecondsRealtime(0.25f);

            if (!LocalizationSettings.HasSettings)
            {
                log.Warning("Localization is not available; keeping Chinese UI and metadata.");
                languageRoutine = null;
                yield break;
            }

            var init = LocalizationSettings.InitializationOperation;
            if (!init.IsValid())
            {
                log.Warning("Localization initialization handle is invalid; keeping Chinese UI.");
                languageRoutine = null;
                yield break;
            }
            if (!init.IsDone) yield return init;

            ModEntry.RefreshLanguage();
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            languageSubscribed = true;
            languageRoutine = null;
        }

        private void OnLocaleChanged(Locale locale)
        {
            if (ModEntry.RefreshLanguage()) ClosePanel();
        }

        private void OnGameLoaded()
        {
            ClosePanel();
            gameReady = true;
            handsBackend?.Invalidate();
            customerBackend?.Invalidate();
            handsBackend = new GameHandsBackend(CanShow);
            hands = new HandsService(handsBackend);
            Tools = ToolRegistry.ForHands(hands);
            customerBackend = new GameCustomerBackend(CanShow, handsBackend);
            new CustomerService(customerBackend).Register(Tools);
            new InspectionService(new GameInspectionBackend(handsBackend)).Register(Tools);
            new DocumentService(new GameDocumentBackend(handsBackend)).Register(Tools);
            ModEntry.NotifyToolsReady(Tools);
            agent = new HandsAgent(transport, Tools, refreshShopContext: true);
            conversation = new ConversationMemory();
            nextBuildAttempt = 0f;
            nextVisitCheck = 0f;
            notifiedVisitId = null;
            TryBuildPortrait();
        }

        private bool InStore()
        {
            return gameReady && StoreUIManager.Instance != null &&
                PlayerStore.Instance.playerLocation == PlayerStore.PlayerLocation.IN_STORE &&
                (MapUIManager.Instance == null || !MapUIManager.Instance.isOutside);
        }

        private bool CanShow()
        {
            if (!InStore()) return false;
            var ui = StoreUIManager.Instance;
            return !ui.isShootingMode &&
                (ui.escapeUIManager == null || !ui.escapeUIManager.menuPanel.activeSelf) &&
                (ui.gameOptionUIManager == null || !ui.gameOptionUIManager.IsOpen) &&
                (ui.blackscreen == null || !ui.blackscreen.gameObject.activeInHierarchy || ui.blackscreen.color.a < 0.05f);
        }

        private void Update()
        {
            if (settings == null) return;
            if (!gameReady) return;
            if (InStore())
            {
                bool typing = StoreUIManager.Instance.IsTyping();
                if (Input.GetKeyDown(settings.ToggleKey.Value) && !typing)
                {
                    sessionHidden = !sessionHidden;
                    log.Log(sessionHidden ? "Portrait hidden (session)." : "Portrait shown (session).");
                }
                if (CanShow() && Input.GetKeyDown(settings.PanelKey.Value) && (!typing || panel != null && panel.IsOpen || handsPanel != null && handsPanel.IsOpen || chatPanel != null && chatPanel.IsOpen)) TogglePanel();
            }
            if ((portrait == null || !portrait.IsAlive) && Time.unscaledTime >= nextBuildAttempt)
            {
                nextBuildAttempt = Time.unscaledTime + 2f;
                if (InStore()) TryBuildPortrait();
            }
            bool canShow = CanShow();
            portrait?.Refresh(settings, canShow && settings.Visible.Value && !sessionHidden);
            if (!canShow && (panel != null || handsPanel != null || chatPanel != null)) ClosePanel();
            if (!canShow) { visitNotice?.Dispose(); visitNotice = null; }
            else
            {
                visitNotice?.Update();
                CheckVisitorNotice();
            }
            if (canShow) chatPanel?.PollInput();
        }

        private void TryBuildPortrait()
        {
            if (portrait != null && portrait.IsAlive || CustomUIManager.Instance == null) return;
            var mod = ModLoader.Mods.Find(m => m.Manifest.ID == ModEntry.ModId);
            if (mod == null || !mod.Textures.TryGetValue("deepseek-npc", out var texture))
            {
                log.Warning("Portrait texture not ready: Textures/deepseek-npc.png. Retrying in two seconds.");
                return;
            }
            var parent = CustomUIManager.Instance.GetLayerRoot(CustomUIManager.LAYER_FRONT);
            portrait = new PortraitView(parent, texture, OpenChat);
            log.Log($"Clickable portrait mounted on front layer ({texture.width}x{texture.height}); transparent pixels pass through.");
        }

        private void TogglePanel()
        {
            if (panel != null && panel.IsOpen || handsPanel != null && handsPanel.IsOpen) { ClosePanel(); return; }
            OpenSettings();
        }

        private void OpenSettings()
        {
            if (panel != null && panel.IsOpen) { panel.Focus(); return; }
            panel?.Dispose();
            panel = new ConnectionWindow(connection, service, OpenHands, OpenChat);
            panel.Show();
        }

        private void ClosePanel()
        {
            chatPanel?.Dispose();
            chatPanel = null;
            handsPanel?.Dispose();
            handsPanel = null;
            panel?.Dispose();
            panel = null;
        }

        private void OpenChat()
        {
            if (!CanShow() || agent == null) return;
            if (chatPanel != null && chatPanel.IsOpen) { chatPanel.Focus(); return; }
            chatPanel?.Dispose();
            chatPanel = new ChatWindow(agent, Tools, connection, conversation, OpenSettings, OpenHands);
            chatPanel.Show();
        }

        private void OpenHands()
        {
            if (handsPanel != null && handsPanel.IsOpen) return;
            handsPanel?.Dispose();
            handsPanel = new HandsWindow(hands, agent, connection);
            handsPanel.Show();
        }

        private void CheckVisitorNotice()
        {
            if (settings == null || !settings.VisitorWarnings.Value || customerBackend == null) return;
            if (Time.unscaledTime < nextVisitCheck) return;
            nextVisitCheck = Time.unscaledTime + 0.25f;
            var visit = customerBackend.Observe();
            if (visit == null || visit.Status == "absent" || string.IsNullOrEmpty(visit.Id)) return;
            if (visit.Id == notifiedVisitId) return;
            // One check per visit, including staff visitors that are intentionally never announced.
            notifiedVisitId = visit.Id;
            var text = VisitorRisk.Decide(visit, null);
            if (string.IsNullOrEmpty(text)) return;
            if (chatPanel != null && chatPanel.IsOpen) chatPanel.PostNotice(text);
            else
            {
                conversation?.PostNotice(text);
                ShowVisitNotice(text);
            }
        }

        private void ShowVisitNotice(string text)
        {
            if (CustomUIManager.Instance == null) return;
            visitNotice?.Dispose();
            visitNotice = new VisitNoticeView(text);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            gameReady = false;
            handsBackend?.Invalidate();
            customerBackend?.Invalidate();
            ClosePanel();
            visitNotice?.Dispose();
            visitNotice = null;
            notifiedVisitId = null;
            portrait?.Dispose();
            portrait = null;
        }

        public void Shutdown()
        {
            if (languageRoutine != null)
            {
                StopCoroutine(languageRoutine);
                languageRoutine = null;
            }
            if (languageSubscribed)
            {
                LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
                languageSubscribed = false;
            }
            if (subscribed)
            {
                ModHook.OnGameLoadedLate -= OnGameLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            gameReady = false;
            handsBackend?.Invalidate();
            customerBackend?.Invalidate();
            ClosePanel();
            visitNotice?.Dispose();
            visitNotice = null;
            notifiedVisitId = null;
            portrait?.Dispose();
            portrait = null;
        }

        private void OnDestroy() => Shutdown();
    }
}
