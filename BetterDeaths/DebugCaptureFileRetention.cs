namespace BetterDeaths;

using System;
using System.IO;
using System.Text;

internal static class DebugCaptureFileRetention
{
    internal const long MaximumBytes = 250L * 1024L * 1024L;
    internal const long RetainedBytes = 200L * 1024L * 1024L;
    private const int BufferBytes = 64 * 1024;

    internal static void TrimIfNeeded(string path, string temporaryPath,
        long maximumBytes = MaximumBytes, long retainedBytes = RetainedBytes)
    {
        if (retainedBytes <= 0 || maximumBytes <= retainedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedBytes));
        }
        if (!File.Exists(path))
        {
            return;
        }

        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (source.Length <= maximumBytes)
            {
                return;
            }

            var preamble = Encoding.UTF8.GetPreamble();
            var start = FindRetainedStart(source, retainedBytes, preamble.Length);
            source.Position = start;
            using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (start > 0)
            {
                destination.Write(preamble);
            }
            source.CopyTo(destination, BufferBytes);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static long FindRetainedStart(FileStream source, long retainedBytes, int preambleBytes)
    {
        // Scan line boundaries in fixed-size blocks, without loading or decoding the log.
        var buffer = new byte[BufferBytes];
        var length = source.Length;
        var retainedStart = length;
        var end = length;
        while (end > 0)
        {
            var start = Math.Max(0, end - buffer.Length);
            var count = (int)(end - start);
            source.Position = start;
            source.ReadExactly(buffer.AsSpan(0, count));
            for (var index = count - 1; index >= 0; index--)
            {
                if (buffer[index] != (byte)'\n' || start + index == length - 1)
                {
                    continue;
                }

                var candidate = start + index + 1;
                if (retainedStart < length && length - candidate + preambleBytes > retainedBytes)
                {
                    return retainedStart;
                }
                retainedStart = candidate;
            }
            end = start;
        }

        // Preserve at least the newest whole record, even if one record exceeds the target.
        return retainedStart == length || length <= retainedBytes ? 0 : retainedStart;
    }
}
