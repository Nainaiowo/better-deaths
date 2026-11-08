using BetterDeaths.PartyCooldowns;
using System.Diagnostics;
using System.Numerics;
using Xunit.Abstractions;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownDisplayTests(ITestOutputHelper output)
{
    private static readonly PartyCooldownAction Action = new(1, "Cooldown", 1, 1, 1, 300, 2, [23]);

    [Theory]
    [InlineData(0, 1, 90, false, "?", "")]
    [InlineData(0, 1, 89.1, true, "90", "")]
    [InlineData(1, 1, 0, true, "", "")]
    [InlineData(1, 2, 12.1, true, "13", "1")]
    [InlineData(0, 2, 12, true, "12", "0")]
    [InlineData(2, 2, 0, true, "", "2")]
    [InlineData(0, 1, 720, true, "720", "")]
    public void CachedPresentationPreservesTimersChargesAndUnknowns(int charges, int maximum, double remaining,
        bool known, string timer, string charge)
    {
        var row = new PartyCooldownDisplayRow([Action]);
        var status = new PartyCooldownStatus(charges, maximum, remaining, known);
        row.Update(0, status);
        var copy = new PartyCooldownDisplayEntry[1];
        Assert.Equal(1, row.CopyTo(copy));
        Assert.Equal(status, copy[0].Status);
        Assert.Equal(timer, copy[0].TimerLabel);
        Assert.Equal(charge, copy[0].ChargeLabel);
    }

    [Fact]
    public void DisplayRowsDoNotShareStateAndCopiesCannotMutateTheCache()
    {
        var first = new PartyCooldownDisplayRow([Action, Action with { Id = 2, Group = 2 }]);
        var second = new PartyCooldownDisplayRow([Action]);
        first.Update(0, new(0, 2, 25, true));
        var buffer = new PartyCooldownDisplayEntry[2];
        Assert.Equal(2, first.CopyTo(buffer));
        Assert.Equal("25", buffer[0].TimerLabel);
        Assert.Equal("?", buffer[1].TimerLabel);
        buffer[0] = default;
        first.CopyTo(buffer);
        Assert.Equal("25", buffer[0].TimerLabel);
        Assert.Equal(1, second.CopyTo(buffer));
        Assert.Equal("?", buffer[0].TimerLabel);
    }

    [Fact]
    public async Task DrawingCannotObserveATornStatusAndLabelDuringConcurrentUpdates()
    {
        var row = new PartyCooldownDisplayRow([Action]);
        var buffer = new PartyCooldownDisplayEntry[1];
        var writer = Task.Run(() =>
        {
            for (var index = 0; index < 10000; index++)
                row.Update(0, new(index % 2, 2, index % 600 + .1, true));
        });
        for (var index = 0; index < 10000; index++)
        {
            row.CopyTo(buffer);
            Assert.Equal(PartyCooldownLabels.Timer(buffer[0].Status), buffer[0].TimerLabel);
            Assert.Equal(PartyCooldownLabels.Charges(buffer[0].Status), buffer[0].ChargeLabel);
        }
        await writer;
    }

    [Fact]
    public void WarmDisplayUpdatesAndReadsAllocateNoManagedMemory()
    {
        var row = new PartyCooldownDisplayRow([Action]);
        var buffer = new PartyCooldownDisplayEntry[12];
        row.Update(0, new(0, 2, 22, true));
        row.CopyTo(buffer);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
        {
            row.Update(0, new(0, 2, index % 600 + .1, true));
            row.CopyTo(buffer);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void NameMeasurementsInvalidateForTextFontNodeAndLayoutChanges()
    {
        var cache = new PartyCooldownNameMeasurement();
        var key = new PartyCooldownNameKey(1, 180, 24, 16, 0, 0, 0, 0, 0);
        Assert.False(cache.TryGet(key, "Player One"u8, out _));
        cache.Store(key, "Player One"u8, new(100, 24));
        Assert.True(cache.TryGet(key, "Player One"u8, out var measured));
        Assert.Equal(new Vector2(100, 24), measured);
        Assert.False(cache.TryGet(key, "Player Two"u8, out _));
        Assert.False(cache.TryGet(key with { Node = 2 }, "Player One"u8, out _));
        Assert.False(cache.TryGet(key with { FontSize = 20 }, "Player One"u8, out _));
        Assert.False(cache.TryGet(key with { Font = 1 }, "Player One"u8, out _));
        Assert.False(cache.TryGet(key with { Width = 200 }, "Player One"u8, out _));
        Assert.False(cache.TryGet(key with { CharacterSpacing = 1 }, "Player One"u8, out _));
    }

    [Fact]
    public void RepeatedNameCacheReadsAllocateNoManagedMemory()
    {
        var cache = new PartyCooldownNameMeasurement();
        var key = new PartyCooldownNameKey(1, 180, 24, 16, 0, 0, 0, 0, 0);
        cache.Store(key, "Player One"u8, new(100, 24));
        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
            matches += cache.TryGet(key, "Player One"u8, out _) ? 1 : 0;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10000, matches);
    }

    [Fact]
    public void EightPlayerPresentationBenchmarkPreservesOutputAndMeasuresAllocations()
    {
        const int frames = 6000;
        _ = RunPresentation(false, 600);
        _ = RunPresentation(true, 600);
        var old = RunPresentation(false, frames);
        var current = RunPresentation(true, frames);
        Assert.Equal(old.Checksum, current.Checksum);
        Assert.True(old.Bytes > 0);
        Assert.Equal(0, current.Bytes);
        output.WriteLine($"8 players x 12 icons, {frames} simulated frames, 10 Hz status refresh:");
        output.WriteLine($"Previous presentation: {old.Milliseconds / frames:F6} ms/frame, {old.Bytes / (double)frames:F1} B/frame.");
        output.WriteLine($"Cached presentation: {current.Milliseconds / frames:F6} ms/frame, {current.Bytes / (double)frames:F1} B/frame.");
        output.WriteLine("Managed presentation only: native UI measurement, game calls, capture, and GPU rendering are excluded.");
    }

    private static (long Checksum, double Milliseconds, long Bytes) RunPresentation(bool cached, int frames)
    {
        var actions = Enumerable.Range(0, 12).Select(index => Action with { Id = (uint)index + 1, Group = (byte)(index + 1) }).ToArray();
        var rows = Enumerable.Range(0, 8).Select(_ => new PartyCooldownDisplayRow(actions)).ToArray();
        var buffer = new PartyCooldownDisplayEntry[12];
        Dictionary<(uint Entity, ulong Member, uint Job, byte Level, uint Action), PartyCooldownStatus> snapshot = [];
        long checksum = 0;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var frame = 0; frame < frames; frame++)
        {
            if (frame % 6 == 0)
            {
                if (!cached)
                    snapshot = [];
                for (var member = 0; member < 8; member++)
                    for (var action = 0; action < 12; action++)
                    {
                        var status = new PartyCooldownStatus(0, action % 3 == 0 ? 2 : 1, 300 - frame / 60d, true);
                        if (cached)
                            rows[member].Update(action, status);
                        else
                            snapshot[((uint)member + 1, (ulong)member + 1, 23, 100, (uint)action + 1)] = status;
                    }
            }
            for (var member = 0; member < 8; member++)
            {
                if (cached)
                    rows[member].CopyTo(buffer);
                for (var action = 0; action < 12; action++)
                {
                    if (cached)
                        checksum += buffer[action].TimerLabel.Length + buffer[action].ChargeLabel.Length;
                    else
                    {
                        var status = snapshot[((uint)member + 1, (ulong)member + 1, 23, 100, (uint)action + 1)];
                        checksum += Math.Ceiling(status.RemainingSeconds).ToString("0").Length;
                        if (status.MaxCharges > 1)
                            checksum += status.Charges.ToString().Length;
                    }
                }
            }
        }
        return (checksum, Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
}
