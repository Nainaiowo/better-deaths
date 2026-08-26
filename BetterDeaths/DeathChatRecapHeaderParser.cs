using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterDeaths;

internal readonly record struct DeathChatRecapHeader(
    int ElapsedSeconds,
    string MemberName,
    string ClassJobName);

internal static class DeathChatRecapHeaderParser
{
    private static readonly Regex HeaderRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool TryParse(string text, out DeathChatRecapHeader header)
    {
        var match = HeaderRegex.Match(text);
        if (!match.Success || !TryParseTimer(match.Groups["timer"].Value, out var elapsedSeconds))
        {
            header = default;
            return false;
        }

        header = new DeathChatRecapHeader(
            elapsedSeconds,
            match.Groups["name"].Value,
            match.Groups["job"].Value);
        return true;
    }

    internal static bool TryCombineFatalLine(
        DeathChatRecapHeader header,
        string text,
        out string combinedPost)
    {
        const string fatalPrefix = "Fatal:";
        if (!text.StartsWith(fatalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            combinedPost = string.Empty;
            return false;
        }

        var fatalDetails = text[fatalPrefix.Length..].TrimStart();
        if (fatalDetails.Length == 0)
        {
            combinedPost = string.Empty;
            return false;
        }

        var timer = $"{header.ElapsedSeconds / 60:00}:{header.ElapsedSeconds % 60:00}";
        combinedPost = $"Recap: {timer} {header.MemberName} ({header.ClassJobName}): {fatalDetails}";
        return true;
    }

    private static bool TryParseTimer(string timer, out int elapsedSeconds)
    {
        var parts = timer.Split(':', 2);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
            seconds is >= 0 and < 60)
        {
            elapsedSeconds = minutes * 60 + seconds;
            return true;
        }

        elapsedSeconds = 0;
        return false;
    }
}
