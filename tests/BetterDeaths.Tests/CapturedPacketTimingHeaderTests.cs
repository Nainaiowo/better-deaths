using BetterDeaths.DamageParsing;
using System.Buffers.Binary;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class CapturedPacketTimingHeaderTests
{
    public static IEnumerable<object[]> CapturedHeaders()
        => LoadExamples().Select(sample => new object[] { sample.Name });

    public static IEnumerable<object[]> CorruptedHeaders()
    {
        DamagePacketTimingRejection[] rejections =
        [
            DamagePacketTimingRejection.IncompleteFrameHeader,
            DamagePacketTimingRejection.IncompleteElementHeader,
            DamagePacketTimingRejection.IpcLengthOutOfRange,
            DamagePacketTimingRejection.FrameLengthOutOfRange,
            DamagePacketTimingRejection.FrameTooShortForPacket,
            DamagePacketTimingRejection.EmptyFrame,
            DamagePacketTimingRejection.CompressedFrame,
            DamagePacketTimingRejection.UnexpectedElementType,
            DamagePacketTimingRejection.ElementLengthMismatch,
            DamagePacketTimingRejection.SourceMismatch,
            DamagePacketTimingRejection.DestinationMismatch,
            DamagePacketTimingRejection.InvalidUnixTimestamp,
            DamagePacketTimingRejection.TimestampOutsideReceiptWindow,
        ];
        foreach (var sample in LoadExamples())
        foreach (var rejection in rejections)
            yield return [sample.Name, (int)rejection];
    }

    [Theory]
    [MemberData(nameof(CapturedHeaders))]
    public async Task CapturedZeroProtocolHeaderPreservesItsExactTimeThroughDeferredHandoff(string name)
    {
        var sample = GetExample(name);
        var frame = Convert.FromHexString(sample.FrameHeaderHex);
        var element = Convert.FromHexString(sample.ElementHeaderHex);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(28)));

        Assert.True(DamagePacketTimingReader.TryRead(frame, element, sample.IpcLength,
            sample.MessageSource, sample.MessageDestination, sample.ReceivedAtUtc, out var time, out var reason));
        Assert.Equal(DamagePacketTimingRejection.None, reason);
        Assert.Equal(sample.ExpectedServerTimeUtc, time);
        Assert.NotEqual(sample.ReceivedAtUtc, time);
        Assert.Equal(sample.FrameHeaderHex, Convert.ToHexString(frame));
        Assert.Equal(sample.ElementHeaderHex, Convert.ToHexString(element));

        // Headers above are captured; buffer identity and IPC fingerprints below are
        // synthetic because the diagnostic intentionally does not retain IPC bodies.
        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 11, 22, time);
        Assert.Equal(sample.ExpectedServerTimeUtc, await Task.Run(() => handoff.Take(100, 11, 22)));
        Assert.Null(handoff.Take(100, 11, 22));
    }

    [Theory]
    [MemberData(nameof(CorruptedHeaders))]
    public void RealHeaderStillRejectsCorruptedMetadata(string name, int rejectionValue)
    {
        var expected = (DamagePacketTimingRejection)rejectionValue;
        var sample = GetExample(name);
        var frame = Convert.FromHexString(sample.FrameHeaderHex);
        var element = Convert.FromHexString(sample.ElementHeaderHex);
        ulong length = sample.IpcLength;
        switch (expected)
        {
            case DamagePacketTimingRejection.IncompleteFrameHeader: frame = frame[..39]; break;
            case DamagePacketTimingRejection.IncompleteElementHeader: element = element[..15]; break;
            case DamagePacketTimingRejection.IpcLengthOutOfRange: length = 65537; break;
            case DamagePacketTimingRejection.FrameLengthOutOfRange:
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24), uint.MaxValue); break;
            case DamagePacketTimingRejection.FrameTooShortForPacket:
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24), (uint)(length + 55)); break;
            case DamagePacketTimingRejection.EmptyFrame: frame[30] = 0; break;
            case DamagePacketTimingRejection.CompressedFrame: frame[33] = 2; break;
            case DamagePacketTimingRejection.UnexpectedElementType: element[12] = 7; break;
            case DamagePacketTimingRejection.ElementLengthMismatch: element[0]++; break;
            case DamagePacketTimingRejection.SourceMismatch: element[4]++; break;
            case DamagePacketTimingRejection.DestinationMismatch: element[8]++; break;
            case DamagePacketTimingRejection.InvalidUnixTimestamp:
                BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16), ulong.MaxValue); break;
            case DamagePacketTimingRejection.TimestampOutsideReceiptWindow:
                BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16),
                    (ulong)new DateTimeOffset(sample.ReceivedAtUtc.AddDays(-2)).ToUnixTimeMilliseconds()); break;
        }

        bool accepted = DamagePacketTimingReader.TryRead(frame, element, length, sample.MessageSource,
            sample.MessageDestination, sample.ReceivedAtUtc, out var time, out var reason);
        Assert.False(accepted);
        Assert.Equal(expected, reason);
        Assert.Equal(default, time);

        var handoff = new DamagePacketTimingHandoff();
        handoff.Publish(100, 11, 22, sample.ExpectedServerTimeUtc);
        handoff.Publish(100, 11, 22, accepted ? time : null);
        Assert.Null(handoff.Take(100, 11, 22));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(65535)]
    public void FrameProtocolValueDoesNotOverrideTheKnownZoneReceivePath(int protocol)
    {
        foreach (var sample in LoadExamples())
        {
            var frame = Convert.FromHexString(sample.FrameHeaderHex);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(28), (ushort)protocol);
            Assert.True(DamagePacketTimingReader.TryRead(frame, Convert.FromHexString(sample.ElementHeaderHex),
                sample.IpcLength, sample.MessageSource, sample.MessageDestination, sample.ReceivedAtUtc,
                out var time, out _));
            Assert.Equal(sample.ExpectedServerTimeUtc, time);
        }
    }

    private static CapturedHeader GetExample(string name) => LoadExamples().Single(sample => sample.Name == name);

    private static CapturedHeader[] LoadExamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CapturedPacketTimingHeaders.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Examples").Deserialize<CapturedHeader[]>()!;
    }

    private sealed record CapturedHeader(string Name, DateTime ReceivedAtUtc, DateTime ExpectedServerTimeUtc,
        ulong IpcLength, uint MessageSource, uint MessageDestination, string FrameHeaderHex, string ElementHeaderHex);
}
