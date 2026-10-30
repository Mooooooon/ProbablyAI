using System;

namespace ProbablyAI.UI
{
    public sealed class AlphaHitMask
    {
        private readonly byte[] alpha;
        private readonly int width;
        private readonly int height;
        public AlphaHitMask(int width, int height, byte[] alpha)
        {
            if (width < 1 || height < 1 || alpha == null || (long)width * height != alpha.Length) throw new ArgumentException("Invalid alpha mask.");
            this.width = width; this.height = height; this.alpha = alpha;
        }
        public bool Hit(float u, float v)
        {
            if (float.IsNaN(u) || float.IsNaN(v) || u < 0f || v < 0f || u >= 1f || v >= 1f) return false;
            return alpha[(int)(v * height) * width + (int)(u * width)] >= 48;
        }
    }
}
