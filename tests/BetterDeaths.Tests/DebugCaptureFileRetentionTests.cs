namespace BetterDeaths.Tests;

using System.Text;
using System.Text.Json;

public sealed class DebugCaptureFileRetentionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"bd-debug-retention-{Guid.NewGuid():N}");
    private string CapturePath => Path.Combine(directory, "capture.jsonl");
    private string TemporaryPath => Path.Combine(directory, "capture.tmp");

    public DebugCaptureFileRetentionTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void ProductionLimitsAre250And200MiB()
    {
        Assert.Equal(250L * 1024L * 1024L, DebugCaptureFileRetention.MaximumBytes);
        Assert.Equal(200L * 1024L * 1024L, DebugCaptureFileRetention.RetainedBytes);
    }

    [Fact]
    public void MissingCaptureDoesNotCreateFiles()
    {
        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath);
        Assert.False(File.Exists(CapturePath));
        Assert.False(File.Exists(TemporaryPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(100)]
    public void CaptureAtOrBelowCapIsUnchanged(int bytes)
    {
        var original = new byte[bytes];
        File.WriteAllBytes(CapturePath, original);
        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, 100, 80);
        Assert.Equal(original, File.ReadAllBytes(CapturePath));
        Assert.False(File.Exists(TemporaryPath));
    }

    [Theory]
    [InlineData("\n", false, false)]
    [InlineData("\n", false, true)]
    [InlineData("\n", true, false)]
    [InlineData("\n", true, true)]
    [InlineData("\r\n", false, false)]
    [InlineData("\r\n", false, true)]
    [InlineData("\r\n", true, false)]
    [InlineData("\r\n", true, true)]
    public void TrimKeepsNewestCompleteUtf8Records(string newline, bool bom, bool finalNewline)
    {
        var rows = Enumerable.Range(0, 50)
            .Select(index => $"{{\"n\":{index},\"text\":\"\u65e5\u672c\ud83c\udf1f\"}}")
            .ToArray();
        File.WriteAllText(CapturePath, string.Join(newline, rows) + (finalNewline ? newline : ""), new UTF8Encoding(bom));

        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, 500, 300);

        var retained = File.ReadAllLines(CapturePath, new UTF8Encoding(false, true));
        Assert.NotEmpty(retained);
        Assert.Equal(rows.TakeLast(retained.Length), retained);
        Assert.True(new FileInfo(CapturePath).Length <= 300);
        Assert.True(new FileInfo(CapturePath).Length + Encoding.UTF8.GetByteCount(rows[rows.Length - retained.Length - 1]) + newline.Length > 300);
        foreach (var row in retained)
        {
            using var parsed = JsonDocument.Parse(row);
            Assert.Equal("\u65e5\u672c\ud83c\udf1f", parsed.RootElement.GetProperty("text").GetString());
        }
        Assert.False(File.Exists(TemporaryPath));
    }

    [Theory]
    [InlineData(65534)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131073)]
    public void LineBoundariesAcrossReadBlocksRemainIntact(int rowLength)
    {
        var rows = Enumerable.Range(0, 4).Select(index => $"{index}:{new string('x', rowLength)}").ToArray();
        File.WriteAllText(CapturePath, string.Join('\n', rows) + "\n", new UTF8Encoding(false));
        var target = Encoding.UTF8.GetByteCount(rows[^1] + "\n") + 3;

        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, target * 2L, target);

        Assert.Equal(new[] { rows[^1] }, File.ReadAllLines(CapturePath));
        Assert.Equal(target, new FileInfo(CapturePath).Length);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OversizedNewestRecordIsNeverSplit(bool earlierRow, bool finalNewline)
    {
        var row = new string('x', 1000);
        File.WriteAllText(CapturePath, (earlierRow ? "older\n" : "") + row + (finalNewline ? "\n" : ""), Encoding.UTF8);

        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, 100, 80);

        Assert.Equal(new[] { row }, File.ReadAllLines(CapturePath));
    }

    [Fact]
    public void RepeatedAppendAndTrimKeepsLatestRows()
    {
        for (var batch = 0; batch < 3; batch++)
        {
            File.AppendAllLines(CapturePath, Enumerable.Range(batch * 50, 50).Select(index => $"{{\"n\":{index}}}"), Encoding.UTF8);
            DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, 400, 300);
            Assert.Equal($"{{\"n\":{batch * 50 + 49}}}", File.ReadAllLines(CapturePath)[^1]);
            Assert.True(new FileInfo(CapturePath).Length <= 300);
        }
    }

    [Fact]
    public void LargeCaptureUsesBoundedManagedMemoryWhileTrimming()
    {
        var row = Encoding.UTF8.GetBytes("{\"text\":\"" + new string('x', 1000) + "\"}\n");
        using (var stream = File.Create(CapturePath))
        {
            for (var index = 0; index < 20000; index++)
            {
                stream.Write(row);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        DebugCaptureFileRetention.TrimIfNeeded(CapturePath, TemporaryPath, 16 * 1024 * 1024, 12 * 1024 * 1024);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"Trimming allocated {allocated:N0} bytes.");
        Assert.True(new FileInfo(CapturePath).Length <= 12 * 1024 * 1024);
        Assert.Equal(row.Length, File.ReadAllLines(CapturePath)[^1].Length + 1);
    }

    [Fact]
    public void FailedTemporaryWriteLeavesOriginalUntouched()
    {
        var original = string.Concat(Enumerable.Repeat("{\"n\":1}\n", 100));
        File.WriteAllText(CapturePath, original, Encoding.UTF8);
        Directory.CreateDirectory(TemporaryPath);

        Assert.ThrowsAny<IOException>(() => DebugCaptureFileRetention.TrimIfNeeded(CapturePath,
            Path.Combine(TemporaryPath, "missing", "file.tmp"), 100, 80));

        Assert.Equal(original, File.ReadAllText(CapturePath));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
