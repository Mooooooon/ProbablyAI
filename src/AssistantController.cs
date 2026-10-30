using ProbablyAI.Services;
using ProbablyAI.UI;
using ProbablyAI.Hands;
using ProbablyAI.Tools;
using ProbablyAI.Customers;
using UnityEngine;
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
        private bool gameReady;
        private bool sessionHidden;
        private float nextBuildAttempt;

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
            ModEntry.NotifyToolsReady(Tools);
            agent = new HandsAgent(transport, Tools);
            conversation = new ConversationMemory();
            nextBuildAttempt = 0f;
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
            if (ModEntry.RefreshLanguage()) ClosePanel();
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

        private void OnSceneUnloaded(Scene scene)
        {
            gameReady = false;
            handsBackend?.Invalidate();
            customerBackend?.Invalidate();
            ClosePanel();
            portrait?.Dispose();
            portrait = null;
        }

        public void Shutdown()
        {
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
            portrait?.Dispose();
            portrait = null;
        }

        private void OnDestroy() => Shutdown();
    }
}
