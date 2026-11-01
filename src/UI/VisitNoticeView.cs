using UnityEngine;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.UI
{
    // A small, non-modal card shown only when the chat window is closed.
    public sealed class VisitNoticeView
    {
        private const float Lifetime = 12f;
        private CustomUIWindow window;
        private float hideAt;

        public VisitNoticeView(string text)
        {
            var builder = CustomUIManager.Window(ModEntry.ModId + ".visit-notice", T("小鲸的提醒"), CustomUIManager.LAYER_FRONT);
            if (builder == null) return;
            builder.SetSize(380f, 200f).SetCloseOnEscape(false);
            window = builder.Handle;
            if (window?.root == null) { window = null; return; }
            window.root.SetActive(false);
            builder.AddLabel(text, "notice").AddButton(T("知道了"), Dispose, "dismiss");
            var label = window.Get("notice")?.text;
            if (label != null)
            {
                label.richText = false;
                label.enableWordWrapping = true;
            }
            builder.Show();
            Place();
            hideAt = Time.unscaledTime + Lifetime;
        }

        public bool IsAlive => window != null && window.IsAlive;

        private void Place()
        {
            var rect = window?.Rect;
            if (rect == null) return;
            // Bottom-right is free of the day counter, customer window, portrait and inventory panel.
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-24f, 24f);
        }

        public void Update()
        {
            if (window == null) return;
            if (!window.IsAlive) { window = null; return; }
            if (Time.unscaledTime >= hideAt) Dispose();
        }

        public void Dispose()
        {
            var closing = window;
            window = null;
            closing?.Close();
        }
    }
}
