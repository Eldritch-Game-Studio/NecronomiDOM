using System;
using System.Diagnostics;
using System.Text.Json;

namespace NecronomiDom.Runtime;

/// <summary>Opt-in inclusive main-thread document costs, caller-thread allocations and process GC deltas.
/// Not exclusive whole-frame/GPU timings; GC pause is not subtracted from the interval.</summary>
internal readonly record struct DocumentUpdateTiming(double ReconcileMs, double ResetMs, double LayoutMs, double PaintMs,
    double TotalMs, ulong StartUsec, ulong EndUsec, long AllocatedBytes, int Gen0, int Gen1, int Gen2, double GcPauseMs);

public partial class DocumentHost
{
    internal event Action<DocumentUpdateTiming>? UpdateMeasured;
    private static readonly bool ObserveEntryUpdates = Environment.GetEnvironmentVariable("NECRONOMIDOM_UPDATE_TIMING") == "1";

    private void PublishUpdateTiming(DocumentUpdateTiming update)
    {
        UpdateMeasured?.Invoke(update);
        // Measure every active document, emit only costly updates. This is diagnostic coverage,
        // never a whole-frame qualification filter; logging/subscribers are outside this span.
        if (ObserveEntryUpdates && (update.TotalMs >= 10 || update.AllocatedBytes >= 1_000_000))
            Godot.GD.Print("NecronomiDOM document-update " + JsonSerializer.Serialize(new
            {
                DocumentPath, NodePath = GetPath().ToString(), ProcessFrame = Godot.Engine.GetProcessFrames(),
                LayoutRevision, Timing = update,
                Coverage = "Active DocumentHost._Process; emitted at >=10ms or >=1000000 caller-thread bytes. " +
                    "Nested allocations/GC are inclusive; logging/subscribers and other callbacks are outside this span."
            }));
    }

    private struct UpdateTimer
    {
        private readonly long _start, _allocated;
        private readonly ulong _startUsec;
        private readonly int _gen0, _gen1, _gen2;
        private readonly double _gcPause;
        private long _last;
        public double ReconcileMs, ResetMs, LayoutMs, PaintMs;
        public UpdateTimer()
        {
            _start = _last = Stopwatch.GetTimestamp();
            _startUsec = Godot.Time.GetTicksUsec();
            _allocated = GC.GetAllocatedBytesForCurrentThread();
            _gen0 = GC.CollectionCount(0); _gen1 = GC.CollectionCount(1); _gen2 = GC.CollectionCount(2);
            _gcPause = GC.GetTotalPauseDuration().TotalMilliseconds;
        }
        public double Mark()
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
            _last = now;
            return elapsed;
        }
        public DocumentUpdateTiming Finish() => new(ReconcileMs, ResetMs, LayoutMs, PaintMs,
            Stopwatch.GetElapsedTime(_start).TotalMilliseconds, _startUsec, Godot.Time.GetTicksUsec(),
            GC.GetAllocatedBytesForCurrentThread() - _allocated,
            GC.CollectionCount(0) - _gen0, GC.CollectionCount(1) - _gen1, GC.CollectionCount(2) - _gen2,
            GC.GetTotalPauseDuration().TotalMilliseconds - _gcPause);
    }
}
