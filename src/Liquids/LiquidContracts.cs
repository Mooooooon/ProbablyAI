using System;
using ProbablyAI.Hands;

namespace ProbablyAI.Liquids
{
    // A read-only view of one real liquid container. The native backend fills it from the
    // same fresh snapshot as the hands tools; tests build it directly without Unity.
    public sealed class LiquidReading
    {
        public ItemInfo Item { get; set; }
        public bool Container { get; set; }
        // Only containers using the game's detailed liquid model (capacity in microliters)
        // can be poured safely; the legacy counter containers do not expose liquid parts.
        public bool Supported { get; set; }
        public bool Owned { get; set; }
        public bool Dropper { get; set; }
        public bool Wine { get; set; }
        // Finished wine natively pours a fixed 750 ml into a container with enough free space.
        public bool WineReady { get; set; }
        public int VolumeUl { get; set; }
        public int CapacityUl { get; set; }
        public int FreeUl => Math.Max(0, CapacityUl - VolumeUl);
        public bool Empty => VolumeUl <= 0;
        public bool Full => CapacityUl > 0 && VolumeUl >= CapacityUl;
    }

    // Before/after readings prove what the native call actually changed. An aborted or
    // filtered pour must never be reported as a completed transfer.
    public sealed class LiquidChange
    {
        public const string SourceToTarget = "source_to_target";
        public const string TargetToSource = "target_to_source";
        public const string SourceToDrain = "source_to_drain";
        public const string FaucetToSource = "faucet_to_source";
        public const string None = "none";

        public LiquidReading SourceBefore { get; set; }
        public LiquidReading SourceAfter { get; set; }
        public LiquidReading TargetBefore { get; set; }
        public LiquidReading TargetAfter { get; set; }
        // Absolute amount that moved, and the absolute amount the receiver gained. A target
        // filter can keep part of the transferred contaminant, so these may differ.
        public int MovedUl { get; set; }
        public int ReceivedUl { get; set; }
        public string Direction { get; set; }

        public int FilteredUl => Direction == SourceToTarget ? Math.Max(0, MovedUl - ReceivedUl) : 0;

        public static LiquidChange MeasurePour(LiquidReading sourceBefore, LiquidReading sourceAfter,
            LiquidReading targetBefore, LiquidReading targetAfter)
        {
            int sourceDelta = (sourceAfter?.VolumeUl ?? sourceBefore.VolumeUl) - sourceBefore.VolumeUl;
            int targetDelta = (targetAfter?.VolumeUl ?? targetBefore.VolumeUl) - targetBefore.VolumeUl;
            var change = new LiquidChange
            {
                SourceBefore = sourceBefore, SourceAfter = sourceAfter,
                TargetBefore = targetBefore, TargetAfter = targetAfter,
                Direction = None
            };
            if (sourceDelta < 0)
            {
                change.Direction = SourceToTarget;
                change.MovedUl = -sourceDelta;
                change.ReceivedUl = Math.Max(0, targetDelta);
            }
            else if (targetDelta < 0)
            {
                // An empty dropper used on a container natively draws liquid instead of pouring.
                change.Direction = TargetToSource;
                change.MovedUl = -targetDelta;
                change.ReceivedUl = Math.Max(0, sourceDelta);
            }
            return change;
        }

        public static LiquidChange MeasureDump(LiquidReading before, LiquidReading after)
            => new LiquidChange
            {
                SourceBefore = before, SourceAfter = after, Direction = SourceToDrain,
                MovedUl = Math.Max(0, before.VolumeUl - after.VolumeUl)
            };

        public static LiquidChange MeasureFill(LiquidReading before, LiquidReading after)
        {
            int added = Math.Max(0, after.VolumeUl - before.VolumeUl);
            return new LiquidChange
            {
                SourceBefore = before, SourceAfter = after, Direction = FaucetToSource,
                MovedUl = added, ReceivedUl = added
            };
        }
    }

    public interface ILiquidBackend
    {
        LiquidReading Inspect(string itemId);
        LiquidChange Pour(string sourceItemId, string targetItemId, bool requireFullTransfer = false);
        LiquidChange Dump(string itemId);
        LiquidChange Fill(string itemId);
    }

    public sealed class LiquidValidation
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
