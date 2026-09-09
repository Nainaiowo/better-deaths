using BetterDeaths.DamageParsing;
using System.Buffers.Binary;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DamagePacketTimingDiagnosticsTests
{
    private static readonly DateTime At = new(2026, 9, 9, 11, 37, 19, DateTimeKind.Utc);

    [Theory]
    [InlineData((int)DamagePacketTimingRejection.IncompleteFrameHeader)]
    [InlineData((int)DamagePacketTimingRejection.IncompleteElementHeader)]
    [InlineData((int)DamagePacketTimingRejection.IpcLengthOutOfRange)]
    [InlineData((int)DamagePacketTimingRejection.FrameLengthOutOfRange)]
    [InlineData((int)DamagePacketTimingRejection.FrameTooShortForPacket)]
    [InlineData((int)DamagePacketTimingRejection.UnexpectedFrameProtocol)]
    [InlineData((int)DamagePacketTimingRejection.EmptyFrame)]
    [InlineData((int)DamagePacketTimingRejection.CompressedFrame)]
    [InlineData((int)DamagePacketTimingRejection.UnexpectedElementType)]
    [InlineData((int)DamagePacketTimingRejection.ElementLengthMismatch)]
    [InlineData((int)DamagePacketTimingRejection.SourceMismatch)]
    [InlineData((int)DamagePacketTimingRejection.DestinationMismatch)]
    [InlineData((int)DamagePacketTimingRejection.InvalidUnixTimestamp)]
    [InlineData((int)DamagePacketTimingRejection.TimestampOutsideReceiptWindow)]
    public void NamesTheExactFailedCheckAndKeepsRejectedTimeUnset(int expectedValue)
    {
        var expected = (DamagePacketTimingRejection)expectedValue;
        var (frame, element) = Headers();
        ulong ipcLength = 32;
        switch (expected)
        {
            case DamagePacketTimingRejection.IncompleteFrameHeader: frame = frame[..39]; break;
            case DamagePacketTimingRejection.IncompleteElementHeader: element = element[..15]; break;
            case DamagePacketTimingRejection.IpcLengthOutOfRange: ipcLength = ulong.MaxValue; break;
            case DamagePacketTimingRejection.FrameLengthOutOfRange: Write32(frame, 24, 39); break;
            case DamagePacketTimingRejection.FrameTooShortForPacket: Write32(frame, 24, 87); break;
            case DamagePacketTimingRejection.UnexpectedFrameProtocol: frame[28] = 0; break;
            case DamagePacketTimingRejection.EmptyFrame: frame[30] = 0; break;
            case DamagePacketTimingRejection.CompressedFrame: frame[33] = 2; break;
            case DamagePacketTimingRejection.UnexpectedElementType: element[12] = 7; break;
            case DamagePacketTimingRejection.ElementLengthMismatch: element[0]++; break;
            case DamagePacketTimingRejection.SourceMismatch: element[4]++; break;
            case DamagePacketTimingRejection.DestinationMismatch: element[8]++; break;
            case DamagePacketTimingRejection.InvalidUnixTimestamp: Write64(frame, 16, 0); break;
            case DamagePacketTimingRejection.TimestampOutsideReceiptWindow:
                Write64(frame, 16, (ulong)new DateTimeOffset(At.AddDays(2)).ToUnixTimeMilliseconds()); break;
        }

        Assert.False(DamagePacketTimingReader.TryRead(frame, element, ipcLength, 10, 20, At, out var time, out var reason));
        Assert.Equal(expected, reason);
        Assert.Equal(default, time);
        Assert.False(DamagePacketTimingReader.TryRead(frame, element, ipcLength, 10, 20, At, out _));
    }

    [Fact]
    public void FirstFailureIsExplicitAndOtherFieldsRemainAvailableForInspection()
    {
        var (frame, element) = Headers();
        frame[28] = 2;
        frame[33] = 2;
        Assert.False(DamagePacketTimingReader.TryRead(frame, element, 32, 10, 20, At, out _, out var reason));
        Assert.Equal(DamagePacketTimingRejection.UnexpectedFrameProtocol, reason);
        var diagnostics = new DamagePacketTimingDiagnostics();
        diagnostics.Record(reason, true, frame, element, 32, 10, 20, At, true, true);
        var example = Assert.Single(Assert.Single(diagnostics.Snapshot()).Examples);
        Assert.Equal(2, example.Frame!.Protocol);
        Assert.Equal(2, example.Frame.Compression);
    }

    [Theory]
    [InlineData(-86400000, true)]
    [InlineData(86400000, true)]
    [InlineData(-86400001, false)]
    [InlineData(86400001, false)]
    [InlineData(-137, true)]
    public void PreservesTimestampAcceptanceBoundaries(int milliseconds, bool valid)
    {
        var (frame, element) = Headers();
        var expectedTime = At.AddMilliseconds(milliseconds);
        Write64(frame, 16, (ulong)new DateTimeOffset(expectedTime).ToUnixTimeMilliseconds());
        Assert.Equal(valid, DamagePacketTimingReader.TryRead(frame, element, 32, 10, 20, At, out var time, out var reason));
        Assert.Equal(valid ? DamagePacketTimingRejection.None : DamagePacketTimingRejection.TimestampOutsideReceiptWindow, reason);
        Assert.Equal(valid ? expectedTime : default, time);
    }

    [Theory]
    [InlineData(16UL)]
    [InlineData(65536UL)]
    public void PreservesIpcLengthAcceptanceBoundaries(ulong length)
    {
        var (frame, element) = Headers();
        Write32(frame, 24, (uint)(length + 56));
        Write32(element, 0, (uint)(length + 16));
        Assert.True(DamagePacketTimingReader.TryRead(frame, element, length, 10, 20, At, out _, out var reason));
        Assert.Equal(DamagePacketTimingRejection.None, reason);
    }

    [Fact]
    public void RetainsOnlyThreeExamplesPerReasonButCountsEveryOccurrence()
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        foreach (var reason in Enum.GetValues<DamagePacketTimingRejection>())
            for (int i = 0; i < 100; i++)
                diagnostics.Record(reason, true, frame, element, 32, 10, 20, At.AddSeconds(i), true, true);

        var summaries = diagnostics.Snapshot();
        Assert.Equal(Enum.GetValues<DamagePacketTimingRejection>().Length, summaries.Length);
        Assert.All(summaries, summary =>
        {
            Assert.Equal(100, summary.Count);
            Assert.Equal(DamagePacketTimingDiagnostics.ExamplesPerReason, summary.Examples.Length);
            Assert.Equal(At, summary.Examples[0].ReceivedAtUtc);
            Assert.Equal(At.AddSeconds(2), summary.Examples[2].ReceivedAtUtc);
        });
    }

    [Fact]
    public void DisabledDetailCaptureDoesNotConsumeTheSampleBudget()
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        for (int i = 0; i < 100; i++)
            Record(diagnostics, false, frame, element);
        Assert.Empty(Assert.Single(diagnostics.Snapshot()).Examples);
        Record(diagnostics, true, frame, element);
        Record(diagnostics, false, frame, element);
        var summary = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(102, summary.Count);
        Assert.Single(summary.Examples);
    }

    [Fact]
    public void CopiesOnlyHeadersWithoutRetainingTheCallersBuffer()
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        var expectedFrame = Convert.ToHexString(frame);
        var expectedElement = Convert.ToHexString(element);
        Array.Resize(ref frame, 4096);
        Array.Resize(ref element, 4096);
        Record(diagnostics, true, frame, element);
        Array.Fill<byte>(frame, 0xFF);
        Array.Fill<byte>(element, 0xFF);

        var example = Assert.Single(Assert.Single(diagnostics.Snapshot()).Examples);
        Assert.Equal(expectedFrame, example.FrameHeaderHex);
        Assert.Equal(expectedElement, example.ElementHeaderHex);
        Assert.Equal(80, example.FrameHeaderHex.Length);
        Assert.Equal(32, example.ElementHeaderHex.Length);
        Assert.Equal(88u, example.Frame!.TotalSize);
        Assert.Equal(48u, example.Element!.Size);
        Assert.Equal(10u, example.MessageSource);
        Assert.Equal(20u, example.MessageDestination);
    }

    [Theory]
    [InlineData((int)DamagePacketTimingRejection.MissingState)]
    [InlineData((int)DamagePacketTimingRejection.MissingFrame)]
    [InlineData((int)DamagePacketTimingRejection.MissingElementHeader)]
    [InlineData((int)DamagePacketTimingRejection.MissingIpcBuffer)]
    public void MissingNativeInputsCanBeReportedWithoutReadingMissingHeaders(int reasonValue)
    {
        var reason = (DamagePacketTimingRejection)reasonValue;
        var diagnostics = new DamagePacketTimingDiagnostics();
        diagnostics.Record(reason, true, [], [], 0, 0, 0, At, false, false);
        var summary = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(reason.ToString(), summary.Reason);
        var example = Assert.Single(summary.Examples);
        Assert.Null(example.Frame);
        Assert.Null(example.Element);
        Assert.Empty(example.FrameHeaderHex);
        Assert.Empty(example.ElementHeaderHex);
        Assert.False(example.HasState);
        Assert.False(example.HasIpcBuffer);
    }

    [Fact]
    public async Task ConcurrentRecordsAndReportsRetainExactCountsAndStayBounded()
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
                Record(diagnostics, true, frame, element);
        }));
        await Task.WhenAll(workers.Append(Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
                Assert.All(diagnostics.Snapshot(), summary => Assert.InRange(summary.Examples.Length, 0, 3));
        })));
        var final = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(8000, final.Count);
        Assert.Equal(3, final.Examples.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SteadyStateCountingDoesNotAllocateOnThePacketThread(bool captureExamples)
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        for (int i = 0; i < 20; i++)
            Record(diagnostics, captureExamples, frame, element);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Record(diagnostics, captureExamples, frame, element);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void SerializedExampleCanReproduceTheReaderDecisionAndSurvivesLaterReports()
    {
        var diagnostics = new DamagePacketTimingDiagnostics();
        var (frame, element) = Headers();
        frame[28] = 0;
        Assert.False(DamagePacketTimingReader.TryRead(frame, element, 32, 10, 20, At, out _, out var reason));
        diagnostics.Record(reason, true, frame, element, 32, 10, 20, At, true, true);
        var json = JsonSerializer.Serialize(diagnostics.Snapshot());
        var decoded = Assert.Single(JsonSerializer.Deserialize<DamagePacketTimingReasonSummary[]>(json)!);
        var example = Assert.Single(decoded.Examples);
        Assert.False(DamagePacketTimingReader.TryRead(Convert.FromHexString(example.FrameHeaderHex),
            Convert.FromHexString(example.ElementHeaderHex), example.IpcLength, example.MessageSource,
            example.MessageDestination, example.ReceivedAtUtc, out _, out var replayedReason));
        Assert.Equal(decoded.Reason, replayedReason.ToString());
        Assert.Equal(json, JsonSerializer.Serialize(diagnostics.Snapshot()));
    }

    private static void Record(DamagePacketTimingDiagnostics diagnostics, bool capture, byte[] frame, byte[] element)
        => diagnostics.Record(DamagePacketTimingRejection.UnexpectedFrameProtocol, capture,
            frame, element, 32, 10, 20, At, true, true);

    // Constructed headers test diagnostics, not the unverified live native layout.
    private static (byte[] Frame, byte[] Element) Headers()
    {
        var frame = new byte[40];
        var element = new byte[16];
        Write64(frame, 16, (ulong)new DateTimeOffset(At.AddMilliseconds(-137)).ToUnixTimeMilliseconds());
        Write32(frame, 24, 88);
        frame[28] = 1;
        frame[30] = 1;
        Write32(element, 0, 48);
        Write32(element, 4, 10);
        Write32(element, 8, 20);
        element[12] = 3;
        return (frame, element);
    }

    private static void Write32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void Write64(byte[] bytes, int offset, ulong value)
        => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
}
