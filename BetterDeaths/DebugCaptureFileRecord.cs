using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BetterDeaths;

// Data must be an owned snapshot: no live game objects or deferred queries over mutable collections.
internal sealed record DebugCaptureFileRecord(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint TerritoryId,
    string TerritoryName,
    string Kind,
    object? Data)
{
    // Reserve this row's position while an owned encounter finishes on a different worker.
    [JsonIgnore]
    public Task<object?>? DeferredData { get; init; }

    public string Serialize(JsonSerializerOptions options)
    {
        if (DeferredData is null)
            return JsonSerializer.Serialize(this, options);

        object? data;
        try
        {
            data = DeferredData.GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            throw new JsonException("Could not complete deferred debug capture data.", error);
        }
        return JsonSerializer.Serialize(this with { Data = data, DeferredData = null }, options);
    }
}
