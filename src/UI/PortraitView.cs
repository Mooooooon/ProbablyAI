using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyAI.UI
{
    // Texture ownership stays with the game's native mod asset loader.
    public sealed class PortraitView
    {
        private readonly GameObject root;
        private readonly RectTransform rect;
        private readonly RectTransform parent;
        private readonly RawImage image;
        private readonly PortraitInteraction interaction;
        public bool IsAlive => root != null;

        public PortraitView(RectTransform parent, Texture2D texture, Action clicked)
        {
            this.parent = parent;
            root = new GameObject("ProbablyAI.DeepSeekPortrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            image = root.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true;
            root.AddComponent<StoreUI>().identifier = "probably_ai.portrait";
            interaction = root.AddComponent<PortraitInteraction>();
            interaction.Initialize(image, texture, clicked);
            root.AddComponent<CustomUITooltipTrigger>().tooltipText = "点击与深空AI聊天";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            root.SetActive(false);
        }

        public void Refresh(AssistantSettings settings, bool visible)
        {
            if (!IsAlive) return;
            root.SetActive(visible);
            if (!visible) return;
            var bounds = parent.rect;
            float left = Mathf.Clamp(settings.Left.Value, 0f, Mathf.Max(0f, bounds.width - 160f));
            float bottom = Mathf.Clamp(settings.Bottom.Value, 0f, Mathf.Max(0f, bounds.height - 160f));
            float aspect = (float)image.texture.width / image.texture.height;
            float height = Mathf.Min(settings.Height.Value, bounds.height - bottom - 20f, (bounds.width - left - 20f) / aspect);
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(height * aspect, height);
            interaction.SetOpacity(Mathf.Clamp01(settings.Opacity.Value));
        }

        public void Dispose()
        {
            if (root == null) return;
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
    }
}
