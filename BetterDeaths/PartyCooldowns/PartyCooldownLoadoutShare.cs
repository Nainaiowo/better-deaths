using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BetterDeaths.PartyCooldowns;

internal sealed class PartyCooldownLoadoutShareDocument
{
    public string Format { get; set; } = PartyCooldownLoadoutShareCodec.Format;
    public int Version { get; set; } = PartyCooldownLoadoutShareCodec.Version;
    public PartyCooldownLoadoutShareLayout? Layout { get; set; }
    public List<PartyCooldownLoadoutShareJob> Jobs { get; set; } = [];
}

internal sealed class PartyCooldownLoadoutShareLayout
{
    public PartyCooldownSide Side { get; set; } = PartyCooldownSide.Left;
    public PartyCooldownLoadoutSharePlacement? Left { get; set; }
    public PartyCooldownLoadoutSharePlacement? Right { get; set; }
}

internal sealed class PartyCooldownLoadoutSharePlacement
{
    public float IconSize { get; set; }
    public float Spacing { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}

internal sealed class PartyCooldownLoadoutShareJob
{
    public uint JobId { get; set; }
    public List<uint> SelectedActionIds { get; set; } = [];
    public bool RowsEnabled { get; set; }
    public List<List<uint>> Rows { get; set; } = [[]];
}

internal static class PartyCooldownLoadoutShareCodec
{
    public const int Version = 2;
    public const int MaximumCodeLength = 32 * 1024;
    private const int LegacyVersion = 1;
    private const string CurrentPrefix = "BDPC2:";
    private const string LegacyPrefix = "BDPC1:";
    public const string Format = "better-deaths-party-cooldowns";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(PartyCooldownLoadoutShareDocument document)
    {
        if (document.Version != Version)
            throw new ArgumentException("Only the current cooldown share format can be exported.", nameof(document));
        if (!TryValidateDocument(document, out var error))
            throw new ArgumentException(string.IsNullOrEmpty(error)
                ? "The cooldown share document is invalid."
                : error, nameof(document));

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var code = CurrentPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        if (code.Length > MaximumCodeLength)
            throw new ArgumentException("The cooldown selection is too large to share.", nameof(document));
        return code;
    }

    public static List<uint> MergeSelectionPreservingLocalOrder(IEnumerable<uint> localSelection,
        IReadOnlyList<uint> sharedSelection)
    {
        var shared = sharedSelection.ToHashSet();
        var merged = localSelection.Where(shared.Contains).Distinct().ToList();
        var retained = merged.ToHashSet();
        merged.AddRange(sharedSelection.Where(retained.Add));
        return merged;
    }

    public static bool TryDecode(string? code, out PartyCooldownLoadoutShareDocument document, out string error)
    {
        document = new();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaximumCodeLength)
        {
            error = "The share code is empty or too long.";
            return false;
        }

        code = code.Trim();
        var (prefix, expectedVersion) = code.StartsWith(CurrentPrefix, StringComparison.Ordinal)
            ? (CurrentPrefix, Version)
            : code.StartsWith(LegacyPrefix, StringComparison.Ordinal)
                ? (LegacyPrefix, LegacyVersion)
                : (string.Empty, 0);
        if (prefix.Length == 0)
        {
            error = "This is not a Better Deaths cooldown share code.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(code[prefix.Length..]));
            var parsed = JsonSerializer.Deserialize<PartyCooldownLoadoutShareDocument>(json, JsonOptions);
            if (parsed is null || parsed.Version != expectedVersion || !TryValidateDocument(parsed, out error))
            {
                if (string.IsNullOrEmpty(error))
                    error = "The share code is incomplete or uses an unsupported format version.";
                return false;
            }

            document = parsed;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            error = "The share code could not be read. Check that it was copied completely.";
            return false;
        }
    }

    public static bool TryValidateDocument(PartyCooldownLoadoutShareDocument? document, out string error)
    {
        error = string.Empty;
        if (document is null || document.Format != Format ||
            document.Version is not (LegacyVersion or Version))
        {
            error = "This cooldown share code uses an unsupported format version.";
            return false;
        }

        var supportedJobs = PartyCooldownCatalog.Jobs.Select(job => job.Id).ToHashSet();
        if (document.Jobs is null || document.Jobs.Count == 0 || document.Jobs.Count > supportedJobs.Count ||
            document.Jobs.Any(job => job is null || !supportedJobs.Contains(job.JobId)) ||
            document.Jobs.Select(job => job.JobId).Distinct().Count() != document.Jobs.Count)
        {
            error = "The code must include one or more unique, supported jobs.";
            return false;
        }

        if (document.Version == Version && !IsValidLayout(document.Layout))
        {
            error = "The code contains invalid party cooldown placement settings.";
            return false;
        }

        foreach (var job in document.Jobs)
        {
            var selected = job.SelectedActionIds;
            if (selected is null || selected.Count > PartyCooldownRows.MaximumAbilities ||
                selected.Any(id => id == 0) || selected.Distinct().Count() != selected.Count)
            {
                error = "One job in the code has an invalid cooldown selection.";
                return false;
            }

            if (document.Version != Version)
                continue;

            if (job.Rows is null || job.Rows.Count == 0 || job.Rows.Count > PartyCooldownRows.MaximumRows ||
                job.Rows.Any(row => row is null))
            {
                error = "One job in the code has an invalid row layout.";
                return false;
            }

            var rowActions = job.Rows.SelectMany(row => row).ToList();
            if (rowActions.Any(id => id == 0) || rowActions.Distinct().Count() != rowActions.Count ||
                !rowActions.ToHashSet().SetEquals(selected))
            {
                error = "One job's rows must contain each selected cooldown exactly once.";
                return false;
            }
        }

        return true;
    }

    public static bool ContainsEverySupportedJob(PartyCooldownLoadoutShareDocument document) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == PartyCooldownCatalog.Jobs.Count;

    public static bool ContainsOnlyJob(PartyCooldownLoadoutShareDocument document, uint jobId) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == 1 && document.Jobs[0].JobId == jobId;

    private static bool IsValidLayout(PartyCooldownLoadoutShareLayout? layout) =>
        layout is not null && Enum.IsDefined(layout.Side) &&
        IsValidPlacement(layout.Left) && IsValidPlacement(layout.Right);

    private static bool IsValidPlacement(PartyCooldownLoadoutSharePlacement? placement) =>
        placement is not null &&
        placement.IconSize == PartyCooldownLayout.IconSize(placement.IconSize) &&
        placement.Spacing == PartyCooldownLayout.Gap(placement.Spacing) &&
        placement.OffsetX == PartyCooldownLayout.Offset(placement.OffsetX) &&
        placement.OffsetY == PartyCooldownLayout.Offset(placement.OffsetY);
}
