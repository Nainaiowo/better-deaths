using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using BetterDeaths.DamageParsing;

namespace BetterDeaths;

internal static class TutorialSampleData
{
    private const string PullResource = "BetterDeaths.TutorialSamples.review-replay-pull.json.gz";
    private const string MeterResource = "BetterDeaths.TutorialSamples.meter-encounter.json.gz";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<Task<PullDeathSnapshot>> Pull = new(() => Task.Run(() => Load<PullDeathSnapshot>(PullResource)));
    private static readonly Lazy<Task<DamageEncounterSnapshot>> Meter = new(() => Task.Run(() => Load<DamageEncounterSnapshot>(MeterResource)));

    internal static Task<PullDeathSnapshot> LoadPullAsync() => Pull.Value;

    internal static Task<DamageEncounterSnapshot> LoadMeterAsync() => Meter.Value;

    internal static PullDeathSnapshot? GetPullIfReady() =>
        Pull.Value.IsCompletedSuccessfully ? Pull.Value.Result : null;

    internal static DamageEncounterSnapshot? GetMeterIfReady() =>
        Meter.Value.IsCompletedSuccessfully ? Meter.Value.Result : null;

    internal static bool PullLoadFailed => Pull.Value.IsFaulted;

    internal static bool MeterLoadFailed => Meter.Value.IsFaulted;

    internal static RecordedPullSummary CreatePullSummary(PullDeathSnapshot pull) => new(
        pull.CapturedAtUtc,
        "Tutorial sample",
        pull.TerritoryId,
        pull.TerritoryName,
        pull.PullElapsedSeconds,
        pull.Deaths.Count)
    {
        PullNumber = 0,
        CapturedPluginVersion = "tutorial",
    };

    internal static bool IsTutorialSummary(RecordedPullSummary summary) =>
        summary.PullNumber == 0 && summary.CapturedPluginVersion == "tutorial";

    private static T Load<T>(string resourceName)
    {
        using var resource = typeof(TutorialSampleData).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Tutorial resource not found: {resourceName}");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<T>(gzip, JsonOptions)
            ?? throw new InvalidDataException($"Tutorial resource is empty: {resourceName}");
    }
}
