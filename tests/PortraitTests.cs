using System;
using System.Threading.Tasks;
using ProbablyAI.UI;

internal static partial class Program
{
    private static async Task RunPortraitTests()
    {
        await Check("portrait breathing stays bounded, smooth and periodic", () =>
        {
            const float period = 4f;
            const float amplitude = 6f;
            Assert(Math.Abs(PortraitBreathing.Offset(0f, period, amplitude)) < 0.001f, "starts off-centre");
            Assert(Math.Abs(PortraitBreathing.Offset(period / 4f, period, amplitude) - amplitude) < 0.001f, "missing peak");
            Assert(Math.Abs(PortraitBreathing.Offset(period / 2f, period, amplitude)) < 0.001f, "not neutral at half cycle");
            Assert(Math.Abs(PortraitBreathing.Offset(period * 3f / 4f, period, amplitude) + amplitude) < 0.001f, "missing trough");
            Assert(Math.Abs(PortraitBreathing.Offset(period, period, amplitude)) < 0.001f, "cycle does not repeat");
            for (int i = 0; i <= 200; i++)
            {
                float offset = PortraitBreathing.Offset(i * period / 200f, period, amplitude);
                Assert(offset >= -amplitude - 0.001f && offset <= amplitude + 0.001f, "exceeds configured distance");
            }
        });
        await Check("portrait breathing disables itself for zero or invalid settings", () =>
        {
            Assert(PortraitBreathing.Offset(1f, 4f, 0f) == 0f, "zero distance still moves");
            Assert(PortraitBreathing.Offset(1f, 0f, 6f) == 0f, "zero cycle still moves");
            Assert(PortraitBreathing.Offset(1f, -2f, 6f) == 0f, "negative cycle still moves");
            Assert(PortraitBreathing.Offset(1f, float.NaN, 6f) == 0f, "NaN cycle still moves");
        });
    }
}
