#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace NecronomiDom.Tests;

/// <summary>Exact current-thread allocation window for zero-allocation assertions measured with
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/>.</summary>
/// <remarks>
/// At the end of a background GC's mark phase the runtime voids every thread's allocation context
/// (gc.cpp background_mark_phase -> repair_allocation_contexts(FALSE) -> void_allocation) without subtracting
/// the unused remainder from alloc_bytes, so the counter jumps by up to one allocation quantum (~8 KB) on a
/// thread that still holds a partly used context. A window measured right after setup allocations therefore
/// fails at random under load. The measurement runs on a fresh thread (no NUnit execution context), after
/// fixed warm-up windows of the exact measured steps, and after blocking collections that retire this thread's
/// context with exact accounting. Only allocations made inside the measured window count; a real per-step
/// allocation still does. Never retry or discard a measured result because it allocated.</remarks>
public static class AllocationWindow
{
    public const int DefaultWarmupWindows = 4;

    /// <summary>Runs <paramref name="warmupWindows"/> unmeasured windows, then one measured window, each
    /// calling <paramref name="step"/> with 0..<paramref name="steps"/>-1, and returns the bytes the
    /// measuring thread allocated inside the measured window. Steps must not call NUnit assertions.
    /// Stateful steps that cannot repeat their inputs pass <c>warmupWindows: 0</c> and warm up before calling.</summary>
    public static long Measure(int steps, Action<int> step, int warmupWindows = DefaultWarmupWindows)
    {
        long allocated = 0;
        ExceptionDispatchInfo? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < warmupWindows; i++) _ = Window(steps, step);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                allocated = Window(steps, step);
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true };
        worker.UnsafeStart();
        worker.Join();
        failure?.Throw();
        return allocated;
    }

    // Optimized up front so no tier/OSR transition of the harness loop falls inside the counters.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long Window(int steps, Action<int> step)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < steps; i++) step(i);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
