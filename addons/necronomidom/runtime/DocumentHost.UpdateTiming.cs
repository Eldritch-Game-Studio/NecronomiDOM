#nullable enable
using System;
using System.Diagnostics;
using System.IO;

namespace NecronomiDom.Runtime;

/// <summary>Opt-in inclusive main-thread document costs, caller-thread allocations and process GC deltas.
/// Not exclusive whole-frame/GPU timings; GC pause is not subtracted from the interval.</summary>
internal readonly record struct DocumentUpdateTiming(double ReconcileMs, double ResetMs, double LayoutMs, double PaintMs,
    double TotalMs, ulong StartUsec, ulong EndUsec, long AllocatedBytes, int Gen0, int Gen1, int Gen2, double GcPauseMs);

public partial class DocumentHost
{
    internal event Action<DocumentUpdateTiming>? UpdateMeasured;
    private static readonly bool ObserveEntryUpdates = Environment.GetEnvironmentVariable("NECRONOMIDOM_UPDATE_TIMING") == "1";
    private StreamWriter? _updateTimingOutput;
    private bool _updateTimingOutputFailed;

    private void PublishUpdateTiming(DocumentUpdateTiming update, string entry)
    {
        UpdateMeasured?.Invoke(update);
        // File output cannot feed a captured game console back into another document update.
        // These are thresholded inclusive phase records, never whole-frame/FPS qualification.
        if (!ObserveEntryUpdates || _updateTimingOutputFailed ||
            update.TotalMs < 10 && update.AllocatedBytes < 1_000_000) return;
        try
        {
            _updateTimingOutput ??= new StreamWriter(Path.Combine(Godot.OS.GetUserDataDir(),
                $"necronomidom-document-updates-{Environment.ProcessId}-{GetInstanceId()}.log"), append: true)
                { AutoFlush = true };
            _updateTimingOutput.WriteLine($"NecronomiDOM document-update {DocumentPath} node={GetPath()} frame={Godot.Engine.GetProcessFrames()} entry={entry} " +
                $"layout={LayoutRevision} {update}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _updateTimingOutputFailed = true;
            CloseUpdateTimingOutput();
            Godot.GD.PushError($"NecronomiDOM timing file output disabled for {DocumentPath}: {error.Message}");
        }
    }

    private void CloseUpdateTimingOutput()
    {
        var output = _updateTimingOutput;
        _updateTimingOutput = null;
        if (output == null) return;
        try { output.Dispose(); }
        catch (IOException error)
        {
            _updateTimingOutputFailed = true;
            Godot.GD.PushError($"NecronomiDOM timing file close failed for {DocumentPath}: {error.Message}");
        }
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
