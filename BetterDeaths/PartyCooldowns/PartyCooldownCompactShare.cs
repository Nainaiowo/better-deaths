using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace BetterDeaths.PartyCooldowns;

// BDPC4 extends the BDPC3 payload with two font percentages before the checksum.
internal static class PartyCooldownCompactShare
{
    private const int RowCountMask = 3;
    private const int RowsEnabled = 4;
    private const int HasRowOrder = 8;
    private const int CheckBytes = 4;

    public static string Encode(PartyCooldownLoadoutShareDocument document)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var layout = document.Layout!;
        writer.Write((byte)layout.Side);
        WritePlacement(writer, layout.Left!);
        WritePlacement(writer, layout.Right!);
        writer.Write((byte)document.Jobs.Count);
        foreach (var job in document.Jobs)
        {
            writer.Write7BitEncodedInt(unchecked((int)job.JobId));
            var selected = job.SelectedActionIds;
            writer.Write((byte)selected.Count);
            foreach (var id in selected)
                writer.Write7BitEncodedInt(unchecked((int)id));

            var reordered = !job.Rows.SelectMany(row => row).SequenceEqual(selected);
            writer.Write((byte)(job.Rows.Count | (job.RowsEnabled ? RowsEnabled : 0) | (reordered ? HasRowOrder : 0)));
            for (var i = 0; i < job.Rows.Count; i++)
            {
                var row = job.Rows[i];
                if (i + 1 < job.Rows.Count)
                    writer.Write((byte)row.Count); // The last row gets the remaining actions.
                if (reordered)
                    foreach (var id in row)
                        writer.Write((byte)selected.IndexOf(id));
            }
        }
        if (layout.FontSizes is { } fonts)
        {
            writer.Write(checked((byte)fonts.LeftPercent));
            writer.Write(checked((byte)fonts.RightPercent));
        }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length), digest);
        writer.Write(digest[..CheckBytes]);
        return Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public static bool TryDecode(byte[] bytes, bool hasFontSizes, out PartyCooldownLoadoutShareDocument document, out CooldownImportError error)
    {
        document = new();
        error = new(CooldownImportProblem.Unreadable);
        var length = bytes.Length - CheckBytes;
        if (length <= 0)
            return false;
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes.AsSpan(0, length), digest);
        if (!digest[..CheckBytes].SequenceEqual(bytes.AsSpan(length)))
            return false;

        using var stream = new MemoryStream(bytes, 0, length, writable: false);
        using var reader = new BinaryReader(stream);
        var parsed = new PartyCooldownLoadoutShareDocument
        {
            Layout = new() { Side = (PartyCooldownSide)reader.ReadByte(), Left = ReadPlacement(reader), Right = ReadPlacement(reader) },
        };
        var jobs = reader.ReadByte();
        if (jobs == 0 || jobs > PartyCooldownCatalog.Jobs.Count)
        {
            error = new(CooldownImportProblem.InvalidJobs);
            return false;
        }
        for (var j = 0; j < jobs; j++)
        {
            var job = new PartyCooldownLoadoutShareJob { JobId = unchecked((uint)reader.Read7BitEncodedInt()), Rows = [] };
            var count = reader.ReadByte();
            if (count > PartyCooldownRows.MaximumAbilities)
            {
                error = new(CooldownImportProblem.InvalidSelection);
                return false;
            }
            for (var i = 0; i < count; i++)
                job.SelectedActionIds.Add(unchecked((uint)reader.Read7BitEncodedInt()));

            var flags = reader.ReadByte();
            var rows = flags & RowCountMask;
            if ((flags & ~(RowCountMask | RowsEnabled | HasRowOrder)) != 0 || rows == 0 || rows > PartyCooldownRows.MaximumRows)
            {
                error = new(CooldownImportProblem.InvalidRows);
                return false;
            }
            job.RowsEnabled = (flags & RowsEnabled) != 0;
            var used = 0;
            for (var r = 0; r < rows; r++)
            {
                var size = r + 1 == rows ? count - used : reader.ReadByte();
                if (size > count - used)
                {
                    error = new(CooldownImportProblem.InvalidRows);
                    return false;
                }
                var row = new List<uint>(size);
                for (var i = 0; i < size; i++, used++)
                {
                    var index = (flags & HasRowOrder) != 0 ? reader.ReadByte() : used;
                    if (index >= count)
                    {
                        error = new(CooldownImportProblem.InvalidRows);
                        return false;
                    }
                    row.Add(job.SelectedActionIds[index]);
                }
                job.Rows.Add(row);
            }
            parsed.Jobs.Add(job);
        }
        if (hasFontSizes)
            parsed.Layout.FontSizes = new(reader.ReadByte(), reader.ReadByte());
        if (stream.Position != stream.Length)
            return false;
        document = parsed;
        error = default;
        return true;
    }

    private static void WritePlacement(BinaryWriter writer, PartyCooldownLoadoutSharePlacement placement)
    {
        writer.Write(placement.IconSize);
        writer.Write(placement.Spacing);
        writer.Write(placement.OffsetX);
        writer.Write(placement.OffsetY);
    }

    private static PartyCooldownLoadoutSharePlacement ReadPlacement(BinaryReader reader) => new()
    {
        IconSize = reader.ReadSingle(),
        Spacing = reader.ReadSingle(),
        OffsetX = reader.ReadSingle(),
        OffsetY = reader.ReadSingle(),
    };
}
