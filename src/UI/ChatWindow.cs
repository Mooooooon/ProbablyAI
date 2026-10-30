using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Services;
using ProbablyAI.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.UI
{
    public sealed class ChatWindow : IDisposable
    {
        private readonly HandsAgent agent;
        private readonly ToolRegistry registry;
        private readonly ConnectionSettingsStore connection;
        private readonly ConversationMemory memory;
        private readonly Action openSettings;
        private readonly Action openHands;
        private readonly List<string> currentActions = new List<string>();
        private CustomUIWindow window;
        private ScrollRect scroll;
        private CancellationTokenSource pending;
        private string currentUser;
        private bool busy;
        public bool IsOpen => window != null && window.IsAlive && window.IsVisible;

        public ChatWindow(HandsAgent agent, ToolRegistry registry, ConnectionSettingsStore connection,
            ConversationMemory memory, Action openSettings, Action openHands)
        {
            this.agent = agent; this.registry = registry; this.connection = connection;
            this.memory = memory; this.openSettings = openSettings; this.openHands = openHands;
        }

        public void Show()
        {
            var b = CustomUIManager.Window(ModEntry.ModId + ".chat", Branding.DisplayName + T(" · 聊天"), CustomUIManager.LAYER_PAPER)
                .SetSize(760f, 770f).SetPosition(new Vector2(170f, 0f)).SetDraggable(true).SetCloseOnEscape();
            window = b.Handle;
            window.root.SetActive(false);
            b.OnClose(OnClosed).BeginScroll(455f);
            scroll = b.CurrentContainer.GetComponentInParent<ScrollRect>(true);
            b.AddLabel("", "conversation").End()
                .AddInput(T("说点什么，比如：帮我把食物放到柜台上"), "", id: "message")
                .AddLabel(T("Enter 发送 · Shift+Enter 换行"))
                .BeginRow()
                    .AddButton(T("发送"), Send, "send")
                    .AddButton(T("停止"), Cancel, "stop")
                    .AddButton(T("连接设置"), () => openSettings?.Invoke())
                    .AddButton(T("手动操作"), () => openHands?.Invoke())
                    .AddButton(T("清空聊天"), Clear, "clear")
                .End()
                .AddLabel(T("准备就绪。"), "status");
            var input = window.Get("message");
            input.input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.input.characterLimit = 6000;
            input.input.richText = false;
            input.input.textComponent.richText = false;
            input.SetPreferredSize(-1f, 90f);
            foreach (var id in new[] { "conversation", "status" })
            {
                var label = window.Get(id).text;
                label.richText = false; label.enableWordWrapping = true;
            }
            SetBusy(false);
            b.Show(); Render();
            input.input.ActivateInputField();
        }

        public void Focus()
        {
            if (!IsOpen) return;
            window.BringToFront();
            window.Get("message").input.ActivateInputField();
        }

        public void PollInput()
        {
            if (!IsOpen || !window.Get("message").input.isFocused) return;
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) Send();
        }

        private async void Send()
        {
            if (!IsOpen || busy) return;
            if (agent.IsRunning) { Status(T("助手正在处理另一个窗口的请求，请等待完成或先停止。")); return; }
            var message = window.Get("message").GetString().Trim();
            if (message.Length == 0) return;
            currentUser = message; currentActions.Clear();
            window.Get("message").SetString("");
            var request = new CancellationTokenSource(); pending = request;
            SetBusy(true); Render();
            try
            {
                await agent.RunAsync(connection.Current, message, Progress, request.Token, memory);
                if (IsOpen) { currentUser = null; currentActions.Clear(); Render(); Status(T("完成。可以继续聊天或安排其他操作。")); }
            }
            catch (OperationCanceledException) { Finished(T("已停止；已经完成的操作保留。")); }
            catch (ApiRequestException error) { Finished(error.Message); }
            catch (HandsException error) { Finished(error.Message); }
            catch (ArgumentException error) { Finished(error.Message); }
            catch (InvalidOperationException error) { Finished(error.Message); }
            catch (Exception) { Finished(T("消息未完成，请检查当前物品状态和连接配置。")); }
            finally
            {
                if (pending == request) pending = null;
                request.Dispose();
                if (IsOpen) SetBusy(false);
            }
        }

        private void Progress(string text)
        {
            if (!IsOpen) return;
            int separator = text.IndexOf('：');
            if (separator < 0) separator = text.IndexOf(':');
            if (separator > 0 && registry.ChangesState(text.Substring(0, separator)))
            {
                currentActions.Add(text.Substring(separator + 1));
                Render();
                Status(T("正在执行你的安排…"));
            }
            else Status(separator > 0 ? T("正在查看当铺中的物品…") : text);
        }

        private void Finished(string status)
        {
            currentUser = null; currentActions.Clear();
            if (IsOpen) { Render(); Status(status); }
        }

        private void Render()
        {
            if (!IsOpen) return;
            var text = new StringBuilder();
            var callNames = new Dictionary<string, string>();
            foreach (JObject message in memory.Messages())
            {
                string role = (string)message["role"];
                if (role == "assistant" && message["tool_calls"] is JArray calls)
                {
                    foreach (var call in calls) callNames[(string)call["id"]] = (string)call["function"]?["name"];
                    continue;
                }
                if (role == "tool")
                {
                    if (callNames.TryGetValue((string)message["tool_call_id"], out var name) && registry.ChangesState(name))
                    {
                        try
                        {
                            var outcome = JObject.Parse((string)message["content"]);
                            text.AppendLine(T("操作：") + (string)outcome["message"]);
                        }
                        catch (JsonException) { }
                    }
                    continue;
                }
                if (role != "user" && role != "assistant") continue;
                string visible = message["content"]?.Type == JTokenType.String ? (string)message["content"] :
                    message["content"] is JArray parts ? string.Concat(parts.OfType<JObject>().Select(p => (string)p["text"])) : null;
                if (!string.IsNullOrWhiteSpace(visible)) text.AppendLine((role == "user" ? T("你：") : T("深空AI：")) + visible).AppendLine();
            }
            if (currentUser != null)
            {
                text.AppendLine(T("你：") + currentUser).AppendLine();
                foreach (var action in currentActions) text.AppendLine(T("操作：") + action);
            }
            if (text.Length == 0) text.Append(T("深空AI：你好！可以和我聊天，也可以让我帮你整理当铺。\n\n例如：帮我把食物放到柜台上。"));
            if (text.Length > 24000) text.Remove(0, text.Length - 24000).Insert(0, T("（仅显示最近的聊天内容）\n"));
            window.Get("conversation").SetText(text.ToString());
            CustomUIBuilder.RebuildLayout(window.ContentRoot);
            Canvas.ForceUpdateCanvases();
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }

        private void Clear()
        {
            if (busy || agent.IsRunning) { Status(T("请先停止当前请求，再清空聊天。")); return; }
            memory.Clear(); currentActions.Clear(); currentUser = null; Render(); Status(T("聊天已清空。"));
        }
        private void SetBusy(bool value)
        {
            busy = value;
            window.Get("send").SetInteractable(!value);
            window.Get("clear").SetInteractable(!value);
            window.Get("stop").SetInteractable(value);
        }
        private void Status(string text) { if (IsOpen) window.Get("status").SetText(text); }
        private void Cancel() => pending?.Cancel();
        private void OnClosed() { Cancel(); window = null; }
        public void Dispose() { Cancel(); var closing = window; window = null; closing?.Close(); }
    }
}
