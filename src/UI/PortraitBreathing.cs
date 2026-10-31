using System;

namespace ProbablyAI.UI
{
    // A slow height change that reads as breathing while the feet stay planted.
    // The value is added to the portrait's drawn height; the view keeps the bottom pivot fixed.
    // Deliberately free of Unity types so the curve can be checked in the test runner.
    public static class PortraitBreathing
    {
        public static float Offset(float time, float period, float amplitude)
        {
            if (!(period > 0.01f) || !(amplitude > 0f)) return 0f;
            return (float)(amplitude * Math.Sin(time * (2.0 * Math.PI / period)));
        }
    }
}
