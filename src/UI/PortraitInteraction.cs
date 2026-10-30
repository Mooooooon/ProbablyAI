using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyAI.UI
{
    public sealed class PortraitInteraction : MonoBehaviour, ICanvasRaycastFilter, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static PortraitInteraction cursorOwner;
        private RawImage image;
        private RectTransform rect;
        private AlphaHitMask mask;
        private Texture2D handCursor;
        private Action clicked;
        private bool hovered;
        private float opacity = 1f;

        public void Initialize(RawImage image, Texture2D texture, Action clicked)
        {
            this.image = image; rect = image.rectTransform; this.clicked = clicked;
            var pixels = texture.GetPixels32();
            var alpha = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
            mask = new AlphaHitMask(texture.width, texture.height, alpha);
            handCursor = CreateHandCursor();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!isActiveAndEnabled || mask == null || rect == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var point)) return false;
            var bounds = rect.rect;
            return bounds.width > 0f && bounds.height > 0f && mask.Hit((point.x - bounds.xMin) / bounds.width, (point.y - bounds.yMin) / bounds.height);
        }

        public void SetOpacity(float value) { opacity = value; ApplyTint(); }
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true; ApplyTint(); cursorOwner = this;
            Cursor.SetCursor(handCursor, new Vector2(9f, 2f), CursorMode.Auto);
        }
        public void OnPointerExit(PointerEventData eventData) => ClearHover();
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging) clicked?.Invoke();
        }
        private void ApplyTint()
        {
            if (image != null) image.color = hovered ? new Color(0.85f, 0.95f, 1f, opacity) : new Color(1f, 1f, 1f, opacity);
        }
        private void ClearHover()
        {
            hovered = false; ApplyTint();
            if (cursorOwner == this) { cursorOwner = null; Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); }
        }
        private void OnDisable() => ClearHover();
        private void OnDestroy() { ClearHover(); if (handCursor != null) Destroy(handCursor); }

        private static Texture2D CreateHandCursor()
        {
            // A small code-drawn UI cursor, independent of character artwork and screen resolution.
            var rows = new[]
            {
                "     XX         ", "    X..X        ", "    X..X        ", "    X..X        ",
                "    X..X        ", "    X..XXX      ", "    X..X..XXX   ", "    X..X..X..XX ",
                "    X..X..X..X.X", "    X..........X", " XX X..........X", "X..XX..........X",
                "X...X..........X", " X.............X", " X.............X", "  X............X",
                "  X...........X ", "   X..........X ", "    X........X  ", "    X........X  ",
                "    XXXXXXXXXX  "
            };
            var pixels = new Color32[32 * 32];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] != ' ') pixels[(31 - (y + 2)) * 32 + x + 4] = rows[y][x] == 'X'
                        ? new Color32(20, 31, 52, 255) : new Color32(239, 248, 255, 255);
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply();
            return texture;
        }
    }
}
