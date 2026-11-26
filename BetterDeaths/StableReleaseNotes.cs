using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BetterDeaths;

internal sealed record StableReleaseNotes(string Version, StableReleaseNotes.Section[] Sections)
{
    internal sealed record Section(string Title, Bullet[] Bullets);
    internal sealed record Bullet(string Text, bool Highlight = false);

    public static IReadOnlyList<StableReleaseNotes> History { get; } = Load();

    private static StableReleaseNotes[] Load()
    {
        using var stream = typeof(StableReleaseNotes).Assembly.GetManifestResourceStream("BetterDeaths.StableReleaseNotes.json")
            ?? throw new InvalidDataException("Missing embedded stable release notes.");
        return JsonSerializer.Deserialize<StableReleaseNotes[]>(stream) is { Length: > 0 } notes
            ? notes : throw new InvalidDataException("Empty stable release notes.");
    }
}
