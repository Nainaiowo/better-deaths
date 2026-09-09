namespace BetterDeaths.DamageParsing;

using System;
using System.Buffers.Binary;

internal static class DamagePacketTimingReader
{
    public static bool TryRead(ReadOnlySpan<byte> frameHeader, ReadOnlySpan<byte> elementHeader,
        ulong ipcLength, uint source, uint destination, DateTime receivedAtUtc, out DateTime time)
    {
        time = default;
        if (frameHeader.Length < 40 || elementHeader.Length < 16 || ipcLength is < 16 or > 65536 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frameHeader[24..]) is < 40 or > 16 * 1024 * 1024 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frameHeader[24..]) < 40 + 16 + ipcLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(frameHeader[28..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(frameHeader[30..]) == 0 || frameHeader[33] != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(elementHeader[12..]) != 3 ||
            BinaryPrimitives.ReadUInt32LittleEndian(elementHeader) != ipcLength + 16 ||
            BinaryPrimitives.ReadUInt32LittleEndian(elementHeader[4..]) != source ||
            BinaryPrimitives.ReadUInt32LittleEndian(elementHeader[8..]) != destination)
            return false;
        return ServerFrameTimestampPolicy.TryConvert(BinaryPrimitives.ReadUInt64LittleEndian(frameHeader[16..]),
            receivedAtUtc, out time);
    }
}
