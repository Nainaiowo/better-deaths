using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DeferredDebugCaptureTests
{
    [Fact]
    public async Task DeferredDataKeepsTheExistingJsonSchemaAndCapturedMetadata()
    {
        var time = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = new DebugCaptureFileRecord(time, 100, 1001, "First territory", "DamageMeterEncounterEnd", null)
        {
            DeferredData = result.Task,
        };
        var data = new { EndedAtUtc = time, Reason = "Combat ended", TotalDamage = 123456 };
        var options = new JsonSerializerOptions();
        var serialize = Task.Run(() => row.Serialize(options));
        Assert.False(serialize.IsCompleted);
        result.SetResult(data);
        var json = await serialize.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((row with { Data = data, DeferredData = null }).Serialize(options), json);
        Assert.DoesNotContain("DeferredData", json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(time, parsed.RootElement.GetProperty("SeenAtUtc").GetDateTime());
        Assert.Equal(1001u, parsed.RootElement.GetProperty("TerritoryId").GetUInt32());
    }

    [Fact]
    public async Task NextPullRowsCannotOvertakeAnEncounterSummary()
    {
        var summary = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new DebugCaptureFileRecord(DateTime.UtcNow, 1, 1, "Duty", "DamageMeterEncounterEnd", null)
            { DeferredData = summary.Task };
        var next = first with { Kind = "DamageMeterAction", Data = new { Amount = 200 }, DeferredData = null };
        var write = Task.Run(() => new[] { first, next }.Select(r => r.Serialize(new JsonSerializerOptions())).ToArray());
        Assert.False(write.IsCompleted);
        summary.SetResult(new { TotalDamage = 1000 });
        var lines = await write.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("DamageMeterEncounterEnd", lines[0]);
        Assert.Contains("DamageMeterAction", lines[1]);
    }

    [Fact]
    public void FailedCompletionUsesTheExistingPerRowSerializationErrorHandling()
    {
        var expected = new InvalidOperationException("Calculation failed");
        var row = new DebugCaptureFileRecord(DateTime.UtcNow, 1, 1, "Duty", "DamageMeterEncounterEnd", null)
            { DeferredData = Task.FromException<object?>(expected) };
        var error = Assert.Throws<JsonException>(() => row.Serialize(new JsonSerializerOptions()));
        Assert.Same(expected, error.InnerException);
    }
}
