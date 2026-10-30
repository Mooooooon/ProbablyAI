using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProbablyAI.Services;
using TMPro;
using UnityEngine;

namespace ProbablyAI.UI
{
    public sealed class ConnectionWindow : IDisposable
    {
        private readonly ConnectionSettingsStore store;
        private readonly IAssistantService service;
        private readonly ConnectionOptions draft;
        private readonly Action openHands;
        private readonly Action openChat;
        private readonly List<string> models = new List<string>();
        private CustomUIWindow window;
        private CancellationTokenSource pending;
        private bool busy;
        public bool IsOpen => window != null && window.IsAlive && window.IsVisible;

        public ConnectionWindow(ConnectionSettingsStore store, IAssistantService service, Action openHands = null, Action openChat = null)
        {
            this.store = store;
            this.service = service;
            this.openHands = openHands;
            this.openChat = openChat;
            draft = store.Current;
        }

        public void Show()
        {
            var builder = CustomUIManager.Window(ModEntry.ModId + ".connection", Branding.ShortName + " · 助手设置", CustomUIManager.LAYER_PAPER)
                .SetSize(680f, 810f).Center().SetDraggable(true).SetCloseOnEscape();
            window = builder.Handle;
            window.root.SetActive(false);
            builder.OnClose(OnClosed)
                .BeginScroll(620f)
                    .AddButton("打开聊天", () => openChat?.Invoke())
                    .AddButton("物品与容器 / 助手操作", () => openHands?.Invoke())
                    .AddHeader("OpenAI 兼容接口")
                    .AddLabel("服务地址（Base URL）")
                    .AddInput("https://api.deepseek.com 或 https://服务地址/v1", draft.BaseUrl,
                        id: "base_url", onTyping: OnBaseUrlChanged)
                    .AddLabel("API Key")
                    .AddInput("填写服务商提供的 API Key；本地服务可留空", draft.ApiKey,
                        id: "api_key", onTyping: OnApiKeyChanged)
                    .AddToggle("记住 API Key（保存在本机文件中，明文）", draft.RememberApiKey,
                        value => draft.RememberApiKey = value, "remember_key")
                    .AddLabel("模型 ID（可手动填写）")
                    .AddInput(ConnectionOptions.DefaultModel, draft.Model, id: "model", onTyping: OnModelChanged)
                    .BeginRow()
                        .AddButton("获取模型列表", FetchModels, "fetch_models")
                        .AddDropdown(new[] { "获取后可在此选择模型" }, 0, SelectModel, "models")
                    .End()
                    .AddSeparator(verticalPadding: 10)
                    .AddHeader("助手提示词")
                    .AddLabel("可编辑完整系统提示词，最多 16000 字。保存后用于之后的聊天与助手操作；留空使用默认值。")
                    .AddInput("留空使用默认女仆助手提示词", "", id: "system_prompt", onTyping: value => draft.SystemPrompt = value)
                    .AddButton("恢复默认提示词", ResetPrompt, "reset_prompt")
                    .AddSeparator(verticalPadding: 10)
                    .AddHeader("测试对话")
                    .AddLabel("使用上方连接与提示词发送测试消息，可在保存前预览效果；不会自动附带存档数据。")
                    .AddInput("输入一条测试消息", "你好，请用中文简单介绍自己。", id: "test_message")
                    .AddButton("发送测试消息", TestMessage, "test")
                    .BeginScroll(145f)
                        .AddLabel("回复将显示在这里。", "reply")
                    .End()
                .End()
                .AddLabel(store.LoadWarning ?? "填写后点击“保存配置”。只有点击获取或发送时才会联网。", "status")
                .BeginRow()
                    .AddButton("保存配置", Save, "save")
                    .AddButton("取消请求", Cancel, "cancel")
                    .AddButton("关闭", Dispose)
                .End();

            ConfigureInput("base_url", 2048);
            ConfigureInput("api_key", 4096, password: true);
            ConfigureInput("model", 256);
            ConfigureInput("system_prompt", AssistantPrompt.MaxLength, multiline: true);
            // Configure multiline before assigning text so TMP does not strip paragraph breaks.
            window.Get("system_prompt").SetString(draft.SystemPrompt);
            ConfigureInput("test_message", 2000);
            SetPlainText("status");
            SetPlainText("reply");
            SetBusy(false);
            builder.Show();
        }

        private void ConfigureInput(string id, int limit, bool password = false, bool multiline = false)
        {
            var element = window.Get(id);
            if (element?.input == null) return;
            element.input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            element.input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            element.input.characterLimit = limit;
            element.input.richText = false;
            if (element.input.textComponent != null) element.input.textComponent.richText = false;
            if (multiline)
            {
                if (element.input.textComponent != null)
                {
                    element.input.textComponent.alignment = TextAlignmentOptions.TopLeft;
                    element.input.textComponent.enableWordWrapping = true;
                }
                if (element.input.placeholder is TMP_Text placeholder) placeholder.alignment = TextAlignmentOptions.TopLeft;
            }
            element.input.ForceLabelUpdate();
            element.SetPreferredSize(-1f, multiline ? 240f : 36f);
        }

        private void ResetPrompt()
        {
            draft.SystemPrompt = AssistantPrompt.Default;
            window.Get("system_prompt").SetString(draft.SystemPrompt);
            Status("已填入默认女仆助手提示词。点击“保存配置”后生效，也可以先发送测试消息预览。");
        }

        private void SetPlainText(string id)
        {
            var label = window.Get(id)?.text;
            if (label == null) return;
            label.richText = false;
            label.enableWordWrapping = true;
        }

        private void OnBaseUrlChanged(string value)
        {
            if (draft.BaseUrl == value) return;
            draft.BaseUrl = value;
            InvalidateModels();
        }

        private void OnApiKeyChanged(string value)
        {
            if (draft.ApiKey == value) return;
            draft.ApiKey = value;
            InvalidateModels();
        }

        private void OnModelChanged(string value)
        {
            draft.Model = value;
            window.Get("models")?.SetIndex(models.IndexOf(value.Trim()) + 1);
        }

        private void InvalidateModels()
        {
            models.Clear();
            window.Get("models")?.SetOptions(new[] { "连接已更改，请重新获取" }).SetIndex(0).SetInteractable(false);
        }

        private void SelectModel(int index)
        {
            if (index <= 0 || index > models.Count) return;
            draft.Model = models[index - 1];
            window.Get("model")?.SetString(draft.Model);
            Status("已选择模型，点击“保存配置”后持久保存。");
        }

        private void FetchModels()
        {
            RunRequest(async cancellation =>
            {
                // Fetching does not require a model to be selected already.
                var options = ReadDraft().Normalized(false);
                Status("正在获取模型列表…");
                var result = await service.ListModelsAsync(options, cancellation);
                if (!IsOpen || cancellation.IsCancellationRequested) return;
                models.Clear();
                models.AddRange(result);
                var choices = new List<string> { "手动输入 / 当前模型" };
                choices.AddRange(models);
                window.Get("models").SetOptions(choices).SetIndex(models.IndexOf(draft.Model.Trim()) + 1);
                Status(models.Count == 0
                    ? "服务返回空列表。仍可手动输入模型 ID 后保存。"
                    : $"已获取 {models.Count} 个模型，请从下拉框选择；当前模型不会自动改变。");
            });
        }

        private void TestMessage()
        {
            RunRequest(async cancellation =>
            {
                var options = ReadDraft().Normalized();
                var message = window.Get("test_message").GetString();
                if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("请先输入测试消息。");
                Status("正在等待模型回复…");
                var reply = await service.ReplyAsync(options, message, null, cancellation);
                if (!IsOpen || cancellation.IsCancellationRequested) return;
                window.Get("reply").SetText(reply.Length > 12000 ? reply.Substring(0, 12000) + "\n（显示已截断）" : reply);
                Status("连接成功，模型已回复。配置需点击“保存配置”后保存。");
                CustomUIBuilder.RebuildLayout(window.ContentRoot);
            });
        }

        private ConnectionOptions ReadDraft()
        {
            // Read widgets at action time, including the field that still has keyboard focus.
            draft.BaseUrl = window.Get("base_url").GetString();
            draft.ApiKey = window.Get("api_key").GetString();
            draft.Model = window.Get("model").GetString();
            draft.SystemPrompt = window.Get("system_prompt").GetString();
            draft.RememberApiKey = window.Get("remember_key").GetBool();
            return draft.Copy();
        }

        private void Save()
        {
            try
            {
                store.Save(ReadDraft());
                var saved = store.Current;
                window.Get("base_url").SetString(saved.BaseUrl);
                window.Get("model").SetString(saved.Model);
                window.Get("system_prompt").SetString(saved.SystemPrompt);
                draft.BaseUrl = saved.BaseUrl;
                draft.Model = saved.Model;
                draft.SystemPrompt = saved.SystemPrompt;
                Status(saved.RememberApiKey
                    ? "配置与提示词已保存，下次发送生效；API Key 保存在本机。"
                    : "配置与提示词已保存，下次发送生效；API Key 仅本次运行有效。");
            }
            catch (ArgumentException error) { Status(error.Message); }
            catch (InvalidOperationException error) { Status(error.Message); }
            catch (Exception) { Status("配置保存失败，请稍后重试。"); }
        }

        private async void RunRequest(Func<CancellationToken, Task> action)
        {
            if (busy || !IsOpen) return;
            var request = new CancellationTokenSource();
            pending = request;
            SetBusy(true);
            try { await action(request.Token); }
            catch (OperationCanceledException) { Status("请求已取消。"); }
            catch (ApiRequestException error) { Status(error.Message); }
            catch (ArgumentException error) { Status(error.Message); }
            catch (Exception) { Status("请求失败，请检查配置后重试。"); }
            finally
            {
                if (pending == request) pending = null;
                request.Dispose();
                if (IsOpen) SetBusy(false);
            }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            foreach (var id in new[] { "base_url", "api_key", "model", "remember_key", "fetch_models", "system_prompt", "reset_prompt", "test_message", "test", "save" })
                window.Get(id)?.SetInteractable(!value);
            window.Get("models")?.SetInteractable(!value && models.Count > 0);
            window.Get("cancel")?.SetInteractable(value);
        }

        private void Status(string text)
        {
            if (IsOpen) window.Get("status")?.SetText(text);
        }

        private void Cancel() => pending?.Cancel();
        public void Focus() { if (IsOpen) window.BringToFront(); }
        private void OnClosed() { Cancel(); window = null; }

        public void Dispose()
        {
            Cancel();
            var closing = window;
            window = null;
            closing?.Close();
        }
    }
}
