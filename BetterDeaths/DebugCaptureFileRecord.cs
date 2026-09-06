using System;
using System.Text.Json;

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
    public string Serialize(JsonSerializerOptions options) => JsonSerializer.Serialize(this, options);
}
