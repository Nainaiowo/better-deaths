using BetterDeaths.DamageParsing;
using System.Buffers.Binary;

namespace BetterDeaths.Tests;

public sealed class DamagePacketTimingHandoffTests
{
    private static readonly DateTime At = new(2026, 9, 9, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CarriesExactTimeAcrossDeferredCrossThreadDispatch()
    {
        var handoff = new DamagePacketTimingHandoff();
        var time = At.AddMilliseconds(137);
        await Task.Run(() => handoff.Publish(100, 1, 2, time));
        Assert.Equal(time, await Task.Run(() => handoff.Take(100, 1, 2)));
        Assert.Null(handoff.Take(100, 1, 2));
        Assert.Equal(1, handoff.Snapshot().Matched);
    }

    [Fact]
    public void IdenticalPacketHeadersInDifferentBuffersRetainTheirOwnTimes()
    {
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 1, 2, At);
        handoff.Publish(200, 1, 2, At.AddMilliseconds(7));
        Assert.Equal(At.AddMilliseconds(7), handoff.Take(200, 1, 2));
        Assert.Equal(At, handoff.Take(100, 1, 2));
    }

    [Fact]
    public void RecycledBufferDoesNotReuseTheOldTimestamp()
    {
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 1, 2, At);
        handoff.Publish(100, 3, 4, At.AddSeconds(1));
        Assert.Equal(At.AddSeconds(1), handoff.Take(100, 3, 4));
        Assert.Equal(1, handoff.Snapshot().Replaced);
    }

    [Fact]
    public void InvalidReplacementInvalidatesPreviousBufferMetadata()
    {
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 1, 2, At);
        handoff.Publish(100, 1, 2, null);
        Assert.Null(handoff.Take(100, 1, 2));
    }

    [Theory]
    [InlineData(2UL, 2UL)]
    [InlineData(1UL, 3UL)]
    public void ChangedHeaderRejectsAndConsumesStaleAssociation(ulong first, ulong second)
    {
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 1, 2, At);
        Assert.Null(handoff.Take(100, first, second));
        Assert.Null(handoff.Take(100, 1, 2));
        Assert.Equal(1, handoff.Snapshot().HeaderMismatches);
    }

    [Fact]
    public void CapacityLossIsBoundedAndReportedWithoutBorrowingANeighborTime()
    {
        var handoff = new DamagePacketTimingHandoff(2);
        handoff.Publish(100, 1, 2, At);
        handoff.Publish(200, 1, 2, At.AddSeconds(1));
        handoff.Publish(300, 1, 2, At.AddSeconds(2));
        Assert.Equal(1, handoff.Snapshot().Evicted);
        Assert.Equal(2, handoff.Snapshot().Pending);
        Assert.Null(handoff.Take(100, 1, 2));
        Assert.Equal(At.AddSeconds(1), handoff.Take(200, 1, 2));
    }

    [Fact]
    public void TerritoryResetRemovesPendingAssociations()
    {
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 1, 2, At);
        handoff.Clear();
        Assert.Null(handoff.Take(100, 1, 2));
        Assert.Equal(0, handoff.Snapshot().Pending);
    }

    [Fact]
    public void EveryPacketInAFrameGetsTheFrameTime()
    {
        var (frame, element) = Headers();
        var handoff = new DamagePacketTimingHandoff();
        for (int i=1; i<=3; i++)
        {
            Assert.True(DamagePacketTimingReader.TryRead(frame, element, 32, 10, 20, At, out var time));
            handoff.Publish(i, 1, 2, time);
        }
        for (int i=3; i>=1; i--)
            Assert.Equal(At.AddMilliseconds(-137), handoff.Take(i, 1, 2));
    }

    [Theory]
    [InlineData(141u)]
    [InlineData(2218u)]
    [InlineData(1825u)]
    [InlineData(2125u)]
    [InlineData(3887u)]
    [InlineData(3889u)]
    public async Task DeferredBuffUpdatesUseTheirPacketTimeAndPreserveRemoval(uint statusId)
    {
        var handoff = new DamagePacketTimingHandoff();
        var ledger = new DamageStatusTimingLedger();
        var source = new DamageActorIdentity(0x10000002, "Provider", 0, "", true, 23);
        const uint target = 0x10000001;
        handoff.Publish(100, 1, 2, At);
        var time = await Task.Run(() => handoff.Take(100, 1, 2));
        Assert.True(ledger.Observe(new(target, 0, new(statusId, source, 0, 30), time!.Value)));
        var staleCountdown = new DamageStatusSnapshot(statusId, source, 0, 5) { StatusSlot = 0 };
        Assert.Equal(0.5f, Assert.Single(ledger.Resolve(target, [staleCountdown], At.AddSeconds(29.5))).RemainingTime);
        handoff.Publish(200, 3, 4, At.AddSeconds(30));
        time = await Task.Run(() => handoff.Take(200, 3, 4));
        ledger.Observe(new(target, 0, new(0, source, 0, 0), time!.Value));
        Assert.Empty(ledger.Resolve(target, [staleCountdown], At.AddSeconds(31)));
        Assert.Equal(0.5f, Assert.Single(ledger.Resolve(target, [staleCountdown], At.AddSeconds(29.5))).RemainingTime);
    }

    [Fact]
    public async Task ConcurrentPacketHandoffsDoNotExchangeTimestamps()
    {
        var handoff = new DamagePacketTimingHandoff();
        await Task.WhenAll(Enumerable.Range(1, 16).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                var time = At.AddTicks(worker * 1000L + i);
                handoff.Publish(worker, (ulong)i, 2, time);
                Assert.Equal(time, handoff.Take(worker, (ulong)i, 2));
            }
        })));
        Assert.Equal(16000, handoff.Snapshot().Matched);
        Assert.Equal(0, handoff.Snapshot().Pending);
        Assert.Equal(0, handoff.Snapshot().Evicted);
    }

    [Theory]
    [InlineData(0)] // Truncated frame.
    [InlineData(1)] // Truncated element.
    [InlineData(2)] // Wrong declared element size.
    [InlineData(3)] // Wrong source.
    [InlineData(4)] // Wrong destination.
    [InlineData(5)] // Non-IPC message.
    [InlineData(6)] // Frame too small for this IPC.
    [InlineData(7)] // Still compressed.
    [InlineData(8)] // No frame packets.
    [InlineData(9)] // Impossible frame size.
    [InlineData(10)] // Invalid timestamp.
    [InlineData(11)] // Short IPC header.
    [InlineData(12)] // Excessive IPC size.
    public void InvalidNativeMetadataCannotCreateAnAuthoritativeTimestamp(int scenario)
    {
        var (frame, element) = Headers();
        ulong length = 32;
        switch (scenario)
        {
            case 0: frame = frame[..39]; break;
            case 1: element = element[..15]; break;
            case 2: element[0]++; break;
            case 3: element[4]++; break;
            case 4: element[8]++; break;
            case 5: element[12] = 7; break;
            case 6: BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24), 87); break;
            case 7: frame[33] = 2; break;
            case 8: frame[30] = 0; break;
            case 9: BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24), uint.MaxValue); break;
            case 10: BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16), 0); break;
            case 11: length = 15; break;
            case 12: length = 65537; break;
        }
        Assert.False(DamagePacketTimingReader.TryRead(frame, element, length, 10, 20, At, out _));
    }

    private static (byte[] Frame, byte[] Element) Headers()
    {
        var frame = new byte[40]; var element = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16), (ulong)new DateTimeOffset(At.AddMilliseconds(-137)).ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24), 184);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(30), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(element, 48);
        BinaryPrimitives.WriteUInt32LittleEndian(element.AsSpan(4), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(element.AsSpan(8), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(element.AsSpan(12), 3);
        return (frame, element);
    }
}
