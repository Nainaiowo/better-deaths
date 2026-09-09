namespace BetterDeaths.DamageParsing;

using System;
using System.Buffers.Binary;

internal enum DamagePacketTimingRejection
{
    None,
    MissingState,
    MissingFrame,
    MissingElementHeader,
    MissingIpcBuffer,
    IncompleteFrameHeader,
    IncompleteElementHeader,
    IpcLengthOutOfRange,
    FrameLengthOutOfRange,
    FrameTooShortForPacket,
    UnexpectedFrameProtocol, // Historical diagnostic identifier; no longer emitted.
    EmptyFrame,
    CompressedFrame,
    UnexpectedElementType,
    ElementLengthMismatch,
    SourceMismatch,
    DestinationMismatch,
    InvalidUnixTimestamp,
    TimestampOutsideReceiptWindow,
    CaptureException,
}

internal static class DamagePacketTimingReader
{
    public static bool TryRead(ReadOnlySpan<byte> frameHeader, ReadOnlySpan<byte> elementHeader,
        ulong ipcLength, uint source, uint destination, DateTime receivedAtUtc, out DateTime time)
        => TryRead(frameHeader, elementHeader, ipcLength, source, destination, receivedAtUtc, out time, out _);

    public static bool TryRead(ReadOnlySpan<byte> frameHeader, ReadOnlySpan<byte> elementHeader,
        ulong ipcLength, uint source, uint destination, DateTime receivedAtUtc, out DateTime time,
        out DamagePacketTimingRejection rejection)
    {
        // The caller is the zone receive hook. The frame protocol field is not a
        // connection discriminator here: captured zone frames legitimately contain zero.
        time = default;
        if (frameHeader.Length < 40)
            rejection = DamagePacketTimingRejection.IncompleteFrameHeader;
        else if (elementHeader.Length < 16)
            rejection = DamagePacketTimingRejection.IncompleteElementHeader;
        else if (ipcLength is < 16 or > 65536)
            rejection = DamagePacketTimingRejection.IpcLengthOutOfRange;
        else if (BinaryPrimitives.ReadUInt32LittleEndian(frameHeader[24..]) is < 40 or > 16 * 1024 * 1024)
            rejection = DamagePacketTimingRejection.FrameLengthOutOfRange;
        else if (BinaryPrimitives.ReadUInt32LittleEndian(frameHeader[24..]) < 40 + 16 + ipcLength)
            rejection = DamagePacketTimingRejection.FrameTooShortForPacket;
        else if (BinaryPrimitives.ReadUInt16LittleEndian(frameHeader[30..]) == 0)
            rejection = DamagePacketTimingRejection.EmptyFrame;
        else if (frameHeader[33] != 0)
            rejection = DamagePacketTimingRejection.CompressedFrame;
        else if (BinaryPrimitives.ReadUInt16LittleEndian(elementHeader[12..]) != 3)
            rejection = DamagePacketTimingRejection.UnexpectedElementType;
        else if (BinaryPrimitives.ReadUInt32LittleEndian(elementHeader) != ipcLength + 16)
            rejection = DamagePacketTimingRejection.ElementLengthMismatch;
        else if (BinaryPrimitives.ReadUInt32LittleEndian(elementHeader[4..]) != source)
            rejection = DamagePacketTimingRejection.SourceMismatch;
        else if (BinaryPrimitives.ReadUInt32LittleEndian(elementHeader[8..]) != destination)
            rejection = DamagePacketTimingRejection.DestinationMismatch;
        else
        {
            var valid = ServerFrameTimestampPolicy.TryConvert(BinaryPrimitives.ReadUInt64LittleEndian(frameHeader[16..]),
                receivedAtUtc, out time, out var timestampRejection);
            rejection = timestampRejection switch
            {
                ServerFrameTimestampRejection.None => DamagePacketTimingRejection.None,
                ServerFrameTimestampRejection.InvalidUnixMilliseconds => DamagePacketTimingRejection.InvalidUnixTimestamp,
                _ => DamagePacketTimingRejection.TimestampOutsideReceiptWindow,
            };
            return valid;
        }
        return false;
    }
}
