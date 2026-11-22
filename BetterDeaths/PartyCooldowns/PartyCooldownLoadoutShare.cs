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
    public List<PartyCooldownLoadoutShareJob> Jobs { get; set; } = [];
}

internal sealed class PartyCooldownLoadoutShareJob
{
    public uint JobId { get; set; }
    public List<uint> SelectedActionIds { get; set; } = [];
}

internal static class PartyCooldownLoadoutShareCodec
{
    public const int Version = 1;
    public const int MaximumCodeLength = 32 * 1024;
    private const string Prefix = "BDPC1:";
    public const string Format = "better-deaths-party-cooldowns";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(PartyCooldownLoadoutShareDocument document)
    {
        if (!TryValidateDocument(document, out var error))
            throw new ArgumentException(error, nameof(document));

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var code = Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
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
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "This is not a Better Deaths cooldown share code.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(code[Prefix.Length..]));
            var parsed = JsonSerializer.Deserialize<PartyCooldownLoadoutShareDocument>(json, JsonOptions);
            if (parsed is null || !TryValidateDocument(parsed, out error))
            {
                if (string.IsNullOrEmpty(error))
                    error = "The share code is incomplete or invalid.";
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
        if (document is null || document.Format != Format || document.Version != Version)
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

        foreach (var job in document.Jobs)
        {
            var selected = job.SelectedActionIds;
            if (selected is null || selected.Count > PartyCooldownRows.MaximumAbilities ||
                selected.Any(id => id == 0) || selected.Distinct().Count() != selected.Count)
            {
                error = "One job in the code has an invalid cooldown selection.";
                return false;
            }

        }

        return true;
    }

    public static bool ContainsEverySupportedJob(PartyCooldownLoadoutShareDocument document) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == PartyCooldownCatalog.Jobs.Count;

    public static bool ContainsOnlyJob(PartyCooldownLoadoutShareDocument document, uint jobId) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == 1 && document.Jobs[0].JobId == jobId;
}
