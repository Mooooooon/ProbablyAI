using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using ProbablyAI.Macros;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.UI
{
    internal sealed class MacroWindow : IDisposable
    {
        private readonly MacroService macros;
        private CustomUIWindow window;
        private ScrollRect editorScroll;
        private IReadOnlyList<MacroStore.Entry> entries;
        private MacroStore.Entry selected;

        internal MacroWindow(MacroService macros) => this.macros = macros;

        internal void Manage()
        {
            Dispose();
            var builder = Create(T("管理宏"), 730f, 750f);
            builder.AddDropdown(new[] { T("新建宏") }, onChanged: Select, id: "entries");
            SetHeight(builder.Last.root, 36f);
            builder.BeginRow();
            SetHeight(builder.CurrentContainer.gameObject, 36f);
            builder
                    .AddButton(T("重新载入宏库"), () => Reload(selected?.Id))
                    .AddButton(T("删除宏"), Delete, "delete")
                .End()
                .BeginScrollFlexible(220f);
            editorScroll = builder.CurrentContainer.GetComponentInParent<ScrollRect>(true);
            builder
                    .AddLabel(T("JSON 定义 · 有待审草稿时显示草稿"))
                    .AddInput("", id: "definition")
                    .AddLabel(T("当前批准版（供对照）"), "approved_label")
                    .AddInput("", id: "approved")
                .End()
                .BeginRow();
            SetHeight(builder.CurrentContainer.gameObject, 36f);
            builder
                    .AddButton(T("保存并启用"), Save, "save")
                    .AddButton(T("批准草稿"), Approve, "approve")
                    .AddButton(T("丢弃草稿"), Discard, "discard")
                .End()
                .AddLabel("", "status");
            ConfigureInput(builder.Handle, "definition", 320f);
            ConfigureInput(builder.Handle, "approved", 185f).readOnly = true;
            builder.Handle.Get("status").text.richText = false;
            builder.Handle.Get("entries").dropdown.captionText.richText = false;
            builder.Handle.Get("entries").dropdown.itemText.richText = false;
            builder.Show();
            Reload(null);
        }

        internal void Inputs(MacroStore.Entry entry, Action<JObject> run)
        {
            Dispose();
            var fields = ((JObject)entry.Approved["inputs"]).Properties().ToArray();
            var builder = Create(T("运行宏"), 570f, Math.Min(690f, 190f + fields.Length * 64f));
            builder.AddLabel(entry.Name).Last.text.richText = false;
            builder.BeginScrollFlexible(70f);
            foreach (var field in fields)
            {
                var spec = (JObject)field.Value;
                string kind = (string)spec["type"];
                builder.AddLabel(field.Name + " · " + T(kind == "boolean" ? "布尔值（true / false）" : kind == "integer" ? "整数" : "文本") +
                    (spec["min"] != null || spec["max"] != null
                        ? " " + F("（{0} 到 {1}）", spec["min"]?.ToString() ?? "−∞", spec["max"]?.ToString() ?? "∞") : ""));
                var initial = spec["default"];
                builder.AddInput("", initial == null ? "" :
                    initial.Type == JTokenType.String ? (string)initial : initial.ToString(Formatting.None), id: "arg_" + field.Name);
                builder.Last.input.characterLimit = 200;
                builder.Last.input.richText = false;
                builder.Last.input.textComponent.richText = false;
            }
            builder.End().AddButton(T("运行宏"), () => Attempt(() =>
            {
                var values = new JObject();
                foreach (var field in fields)
                {
                    string text = window.Get("arg_" + field.Name).GetString();
                    switch ((string)field.Value["type"])
                    {
                        case "string": values[field.Name] = text; break;
                        case "integer":
                            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                                throw MacroDefinition.Invalid("inputs." + field.Name);
                            values[field.Name] = number;
                            break;
                        case "boolean":
                            if (!bool.TryParse(text, out bool flag)) throw MacroDefinition.Invalid("inputs." + field.Name);
                            values[field.Name] = flag;
                            break;
                    }
                }
                MacroDefinition.BindInputs(entry.Approved, values);
                Dispose();
                run(values);
            })).AddLabel("", "status");
            builder.Handle.Get("status").text.richText = false;
            builder.Show();
        }

        private CustomUIBuilder Create(string title, float width, float height)
        {
            var builder = CustomUIManager.Window(ModEntry.ModId + ".macros", title, CustomUIManager.LAYER_PAPER)
                .SetSize(width, height).Center().SetDraggable(true).SetCloseOnEscape();
            window = builder.Handle;
            return builder.OnClose(() => window = null);
        }

        private static TMP_InputField ConfigureInput(CustomUIWindow handle, string id, float height)
        {
            var field = handle.Get(id);
            SetHeight(field.root, height);
            field.SetFlexible(1f, 0f);
            var input = field.input;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.characterLimit = MacroDefinition.MaxEditorCharacters;
            input.richText = false;
            input.textComponent.richText = false;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            input.textComponent.enableWordWrapping = true;
            var viewport = input.textViewport;
            if (viewport != null && viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            return input;
        }

        private static void SetHeight(GameObject root, float height)
        {
            var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private void Reload(string id)
        {
            entries = macros.Store.List();
            window.Get("entries").SetOptions(new[] { T("新建宏") }.Concat(entries.Select(entry =>
                entry.Name + " · " + entry.Id.Substring(0, 6) + (entry.Draft == null ? "" : " · " + T("待审")))).ToArray());
            int index = id == null ? 0 : Array.FindIndex(entries.ToArray(), entry => entry.Id == id) + 1;
            window.Get("entries").SetIndex(index);
            Select(index);
        }

        private void Select(int index)
        {
            if (entries == null) return;
            selected = index > 0 && index <= entries.Count ? entries[index - 1] : null;
            bool draft = selected?.Draft != null;
            var definition = selected?.Draft ?? selected?.Approved;
            var definitionField = window.Get("definition");
            var approvedField = window.Get("approved");
            definitionField.SetString(EditorText(definition) ??
                "{\n  \"version\": 1,\n  \"name\": \"\",\n  \"inputs\": {},\n  \"steps\": []\n}");
            bool compare = draft && selected.Approved != null;
            approvedField.SetVisible(compare).SetString(compare ? EditorText(selected.Approved) : "");
            window.Get("approved_label").SetVisible(compare);
            window.Get("approve").SetInteractable(draft);
            window.Get("discard").SetInteractable(draft);
            window.Get("delete").SetInteractable(selected != null);
            window.Get("save").SetInteractable(!macros.Store.LoadFailed);
            window.Get("status").SetText(macros.Store.Warning ?? "");
            CustomUIBuilder.RebuildLayout(window.ContentRoot);
            if (editorScroll != null)
            {
                editorScroll.StopMovement();
                editorScroll.verticalNormalizedPosition = 1f;
            }
            ResetEditorScroll(definitionField.input);
            if (compare) ResetEditorScroll(approvedField.input);
        }

        private static void ResetEditorScroll(TMP_InputField input)
        {
            input.MoveTextStart(false);
            var text = input.textComponent;
            var position = text.rectTransform.anchoredPosition;
            text.rectTransform.anchoredPosition = new Vector2(position.x, 0f);
            input.ForceLabelUpdate();
            var scrollbar = input.verticalScrollbar;
            if (scrollbar == null || input.textViewport == null) return;
            // TMP can retain a full-height thumb after switching from a short document.
            float viewportHeight = input.textViewport.rect.height;
            float textHeight = text.preferredHeight;
            float size = textHeight > viewportHeight && viewportHeight > 0f ? viewportHeight / textHeight : 1f;
            if (size < 1f) scrollbar.gameObject.SetActive(true);
            scrollbar.size = size;
            scrollbar.value = 0f;
        }

        private void Save() => Attempt(() =>
        {
            var definition = MacroDefinition.Parse(window.Get("definition").GetString());
            var entry = macros.Save(selected?.Id, selected?.Revision, definition, draft: false);
            Reload(entry.Id);
            window.Get("status").SetText(T("宏已保存并启用。"));
        });

        private static string EditorText(JObject definition)
        {
            string formatted = definition?.ToString(Formatting.Indented);
            return formatted?.Length > MacroDefinition.MaxEditorCharacters ? definition.ToString(Formatting.None) : formatted;
        }

        private void Approve() => Attempt(() =>
        {
            if (!JToken.DeepEquals(MacroDefinition.Parse(window.Get("definition").GetString()), selected.Draft))
                throw new HandsException("macro_draft_edited", T("编辑器有改动，请保存并启用，或重新载入草稿后批准。"));
            macros.Approve(selected.Id, selected.Revision);
            Reload(selected.Id);
            window.Get("status").SetText(T("草稿已批准。"));
        });

        private void Discard() => Attempt(() =>
        {
            macros.Store.Discard(selected.Id, selected.Revision);
            Reload(selected.Id);
        });

        private void Delete() => Attempt(() =>
        {
            macros.Store.Delete(selected.Id, selected.Revision);
            Reload(null);
        });

        private void Attempt(Action action)
        {
            try { action(); }
            catch (HandsException error) { window?.Get("status").SetText(error.Message); }
            catch (JsonException error) { window?.Get("status").SetText(T("JSON 格式无效。") + " " + error.Message); }
        }

        public void Dispose()
        {
            var closing = window;
            window = null;
            closing?.Close();
        }
    }
}
