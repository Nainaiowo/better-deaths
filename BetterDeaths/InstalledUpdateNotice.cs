using System;

namespace BetterDeaths;

internal sealed class InstalledUpdateNotice
{
    private bool completed;
    private bool delivering;

    public string? VersionToRecord { get; }
    public bool ShouldAnnounce { get; }
    public bool Pending => !completed && VersionToRecord is not null;

    public InstalledUpdateNotice(Version? loadedVersion, string? recordedVersion, bool hadSavedConfiguration)
    {
        if (loadedVersion is null)
            return;
        var current = Normalize(loadedVersion);
        if (current == new Version(0, 0, 0, 0))
            return;
        if (!hadSavedConfiguration || !Version.TryParse(recordedVersion, out var previous))
        {
            VersionToRecord = current.ToString(4);
            // Only the first rollout uses existing settings as evidence of an upgrade.
            ShouldAnnounce = hadSavedConfiguration && string.IsNullOrWhiteSpace(recordedVersion);
        }
        else if (current > Normalize(previous))
        {
            VersionToRecord = current.ToString(4);
            ShouldAnnounce = true;
        }
    }

    public bool TryDeliver(bool loggedIn, Action<string> persistVersion, Action<string> print)
    {
        if (!Pending || delivering || ShouldAnnounce && !loggedIn)
            return false;
        delivering = true;
        try
        {
            persistVersion(VersionToRecord!);
            if (completed)
                return false;
            // Persist first so reloading cannot repeat an already queued chat message.
            completed = true;
            if (ShouldAnnounce)
                print(VersionToRecord!);
            return ShouldAnnounce;
        }
        finally
        {
            delivering = false;
        }
    }

    public void Cancel() => completed = true;

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
