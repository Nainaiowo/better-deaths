using System;
using System.Diagnostics;

namespace BetterDeaths.PartyCooldowns;

internal enum PartyCooldownPhase { Update, Draw, Capture }

internal readonly record struct PartyCooldownPerformance(long Samples, double AverageMs, double RecentP99Ms,
    double MaximumMs, double BytesPerCall);

internal sealed class PartyCooldownProfiler
{
    private readonly Counter[] counters = [new(), new(), new()];

    public Sample Measure(PartyCooldownPhase phase) => new(counters[(int)phase]);
    public PartyCooldownPerformance Get(PartyCooldownPhase phase) => counters[(int)phase].Get();

    public void Reset()
    {
        foreach (var counter in counters)
            counter.Reset();
    }

    internal readonly struct Sample : IDisposable
    {
        private readonly Counter? counter;
        private readonly long start;
        private readonly long allocated;

        internal Sample(Counter counter)
        {
            this.counter = counter;
            allocated = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (counter is not null)
                counter.Record(Stopwatch.GetTimestamp() - start, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
    }

    internal sealed class Counter
    {
        private readonly object sync = new();
        private readonly long[] recent = new long[1024];
        private long samples, ticks, maximum, allocated;

        public void Record(long elapsed, long bytes)
        {
            lock (sync)
            {
                recent[(int)(samples % recent.Length)] = elapsed;
                samples++;
                ticks += elapsed;
                maximum = Math.Max(maximum, elapsed);
                allocated += bytes;
            }
        }

        public PartyCooldownPerformance Get()
        {
            lock (sync)
            {
                if (samples == 0)
                    return default;
                var count = (int)Math.Min(samples, recent.Length);
                Span<long> ordered = stackalloc long[count];
                recent.AsSpan(0, count).CopyTo(ordered);
                ordered.Sort();
                var scale = 1000d / Stopwatch.Frequency;
                return new(samples, ticks * scale / samples,
                    ordered[(int)Math.Ceiling(count * .99) - 1] * scale, maximum * scale, allocated / (double)samples);
            }
        }

        public void Reset()
        {
            lock (sync)
                samples = ticks = maximum = allocated = 0;
        }
    }
}
