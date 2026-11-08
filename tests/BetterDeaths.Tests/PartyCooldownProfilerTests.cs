using BetterDeaths.PartyCooldowns;
using System.Diagnostics;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownProfilerTests
{
    [Fact]
    public void CounterReportsAverageRecentPercentileMaximumAndAllocations()
    {
        var counter = new PartyCooldownProfiler.Counter();
        Assert.Equal(default, counter.Get());
        for (var index = 1; index <= 100; index++)
            counter.Record(index * Stopwatch.Frequency / 1000, index * 10);
        var stats = counter.Get();
        Assert.Equal(100, stats.Samples);
        Assert.Equal(50.5, stats.AverageMs, 3);
        Assert.Equal(99, stats.RecentP99Ms, 3);
        Assert.Equal(100, stats.MaximumMs, 3);
        Assert.Equal(505, stats.BytesPerCall);
        counter.Reset();
        Assert.Equal(default, counter.Get());
    }

    [Fact]
    public void CaptureDrawAndUpdateMetricsRemainSeparate()
    {
        var profiler = new PartyCooldownProfiler();
        using (profiler.Measure(PartyCooldownPhase.Update)) { }
        Assert.Equal(1, profiler.Get(PartyCooldownPhase.Update).Samples);
        Assert.Equal(0, profiler.Get(PartyCooldownPhase.Draw).Samples);
        Assert.Equal(0, profiler.Get(PartyCooldownPhase.Capture).Samples);
        profiler.Reset();
        Assert.Equal(0, profiler.Get(PartyCooldownPhase.Update).Samples);
    }

    [Fact]
    public void WarmMeasurementScopesAllocateNoManagedMemory()
    {
        var profiler = new PartyCooldownProfiler();
        using (profiler.Measure(PartyCooldownPhase.Draw)) { }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
            using (profiler.Measure(PartyCooldownPhase.Draw)) { }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }
}
