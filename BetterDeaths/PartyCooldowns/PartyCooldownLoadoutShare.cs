using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PartyCooldownShareFontSizes? FontSizes { get; set; }
}

internal sealed record PartyCooldownShareFontSizes(int LeftPercent, int RightPercent);

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
    private const string FontSizePrefix = "BDPC4:";
    private const string CompactPrefix = "BDPC3:";
    private const string JsonPrefix = "BDPC2:";
    private const string LegacyPrefix = "BDPC1:";
    public const string Format = "better-deaths-party-cooldowns";

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions(legacy: false);
    private static readonly JsonSerializerOptions LegacyOptions = CreateOptions(legacy: true);

    private static JsonSerializerOptions CreateOptions(bool legacy)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            if (type.Kind != JsonTypeInfoKind.Object)
                return;
            for (var i = type.Properties.Count - 1; i >= 0; i--)
            {
                var field = type.Properties[i];
                // Font sizes only exist in the compact BDPC4 payload, never in the released JSON schemas.
                if (field.Name == "fontSizes" || (legacy && field.Name is "layout" or "rows" or "rowsEnabled"))
                    type.Properties.RemoveAt(i);
                else
                    field.IsRequired = true;
            }
        });
        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            MaxDepth = 12,
        };
    }

    public static bool TryEncode(PartyCooldownLoadoutShareDocument document, out string code, out string error)
    {
        code = string.Empty;
        if (document.Version != Version)
        {
            error = "Only the current cooldown share format can be exported.";
            return false;
        }
        if (!TryValidateDocument(document, out var invalid))
        {
            error = invalid.Reason;
            return false;
        }

        var prefix = document.Layout!.FontSizes is null ? CompactPrefix : FontSizePrefix;
        var encoded = prefix + PartyCooldownCompactShare.Encode(document);
        if (encoded.Length > MaximumCodeLength)
        {
            error = "The cooldown selection is too large to share.";
            return false;
        }
        code = encoded;
        error = string.Empty;
        return true;
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

    public static bool TryDecode(string? code, out PartyCooldownLoadoutShareDocument document, out CooldownImportError error)
    {
        document = new();
        if (code?.Length > MaximumCodeLength)
        {
            error = new(CooldownImportProblem.TooLong);
            return false;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            error = new(CooldownImportProblem.EmptyClipboard);
            return false;
        }

        code = code.Trim();
        var hasFontSizes = code.StartsWith(FontSizePrefix, StringComparison.Ordinal);
        var compact = hasFontSizes || code.StartsWith(CompactPrefix, StringComparison.Ordinal);
        var (prefix, expectedVersion) = compact ? (hasFontSizes ? FontSizePrefix : CompactPrefix, Version)
            : code.StartsWith(JsonPrefix, StringComparison.Ordinal) ? (JsonPrefix, Version)
            : code.StartsWith(LegacyPrefix, StringComparison.Ordinal)
                ? (LegacyPrefix, LegacyVersion)
                : (string.Empty, 0);
        if (prefix.Length == 0)
        {
            var colon = code.IndexOf(':');
            var numbered = colon > 4 && code.StartsWith("BDPC", StringComparison.Ordinal) &&
                code.AsSpan(4, colon - 4).IndexOfAnyExceptInRange('0', '9') < 0;
            error = new(numbered ? CooldownImportProblem.UnsupportedVersion : CooldownImportProblem.NotCooldownCode);
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(code[prefix.Length..]);
            PartyCooldownLoadoutShareDocument? parsed;
            if (compact)
            {
                if (!PartyCooldownCompactShare.TryDecode(bytes, hasFontSizes, out parsed, out error))
                    return false;
            }
            else
                parsed = JsonSerializer.Deserialize<PartyCooldownLoadoutShareDocument>(bytes,
                    expectedVersion == LegacyVersion ? LegacyOptions : JsonOptions);
            if (parsed is null || parsed.Version != expectedVersion)
            {
                error = new(parsed is null || parsed.Version is LegacyVersion or Version
                    ? CooldownImportProblem.Unreadable : CooldownImportProblem.UnsupportedVersion);
                return false;
            }
            if (!TryValidateDocument(parsed, out error))
                return false;

            document = parsed;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or EndOfStreamException)
        {
            error = new(CooldownImportProblem.Unreadable);
            return false;
        }
    }

    public static bool TryReadClipboardCode(nint clipboard, out string code, out CooldownImportError error)
    {
        // ImGui owns a NUL-terminated UTF-8 string. Stop at the limit instead of measuring the entire clipboard.
        var length = 0;
        if (clipboard != 0)
            while (length <= MaximumCodeLength && Marshal.ReadByte(clipboard, length) != 0)
                length++;
        if (length == 0 || length > MaximumCodeLength)
        {
            code = string.Empty;
            error = new(length == 0 ? CooldownImportProblem.EmptyClipboard : CooldownImportProblem.TooLong);
            return false;
        }
        code = Marshal.PtrToStringUTF8(clipboard, length)!;
        error = default;
        return true;
    }

    public static bool TryValidateDocument(PartyCooldownLoadoutShareDocument? document, out CooldownImportError error)
    {
        error = default;
        if (document is null || document.Format != Format)
        {
            error = new(document is null ? CooldownImportProblem.Unreadable : CooldownImportProblem.NotCooldownCode);
            return false;
        }
        if (document.Version is not (LegacyVersion or Version))
        {
            error = new(CooldownImportProblem.UnsupportedVersion);
            return false;
        }

        var supportedJobs = PartyCooldownCatalog.Jobs.Select(job => job.Id).ToHashSet();
        if (document.Jobs is null || document.Jobs.Count == 0 || document.Jobs.Count > supportedJobs.Count ||
            document.Jobs.Any(job => job is null || !supportedJobs.Contains(job.JobId)) ||
            document.Jobs.Select(job => job.JobId).Distinct().Count() != document.Jobs.Count)
        {
            error = new(CooldownImportProblem.InvalidJobs);
            return false;
        }

        if (document.Version == Version)
        {
            if (document.Layout is not { Left: { } left, Right: { } right } layout || !Enum.IsDefined(layout.Side))
            {
                error = new(CooldownImportProblem.InvalidPlacement);
                return false;
            }
            if (!ValidatePlacement(left, "Left", out error) || !ValidatePlacement(right, "Right", out error))
                return false;
            if (layout.FontSizes is { } fonts &&
                (!InRange("Left", "font size", fonts.LeftPercent, PartyCooldownLayout.MinimumFontPercent, PartyCooldownLayout.MaximumFontPercent, out error, "%") ||
                 !InRange("Right", "font size", fonts.RightPercent, PartyCooldownLayout.MinimumFontPercent, PartyCooldownLayout.MaximumFontPercent, out error, "%")))
                return false;
        }

        foreach (var job in document.Jobs)
        {
            var selected = job.SelectedActionIds;
            if (selected is null || selected.Count > PartyCooldownRows.MaximumAbilities ||
                selected.Any(id => id == 0) || selected.Distinct().Count() != selected.Count)
            {
                error = new(CooldownImportProblem.InvalidSelection);
                return false;
            }

            if (document.Version != Version)
                continue;

            if (job.Rows is null || job.Rows.Count == 0 || job.Rows.Count > PartyCooldownRows.MaximumRows ||
                job.Rows.Any(row => row is null))
            {
                error = new(CooldownImportProblem.InvalidRows);
                return false;
            }

            var rowActions = job.Rows.SelectMany(row => row).ToList();
            if (rowActions.Any(id => id == 0) || rowActions.Distinct().Count() != rowActions.Count ||
                !rowActions.ToHashSet().SetEquals(selected))
            {
                error = new(CooldownImportProblem.InvalidRows, "A job's rows must contain each selected cooldown exactly once.");
                return false;
            }
        }

        return true;
    }

    public static bool ContainsEverySupportedJob(PartyCooldownLoadoutShareDocument document) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == PartyCooldownCatalog.Jobs.Count;

    public static bool ContainsOnlyJob(PartyCooldownLoadoutShareDocument document, uint jobId) =>
        TryValidateDocument(document, out _) && document.Jobs.Count == 1 && document.Jobs[0].JobId == jobId;

    public static bool TryImport(string? code, uint? targetJobId, PartyCooldownCatalog catalog,
        Dictionary<uint, List<uint>> selections, Dictionary<uint, PartyCooldownRows> rowLayouts,
        out PartyCooldownLoadoutShareLayout? layout, out CooldownImportError error)
    {
        layout = null;
        if (!TryDecode(code, out var document, out error))
            return false;
        if (targetJobId is { } jobId ? !ContainsOnlyJob(document, jobId) : !ContainsEverySupportedJob(document))
        {
            var instruction = document.Jobs.Count == 1
                ? $"Use Import inside {PartyCooldownCatalog.Jobs.First(job => job.Id == document.Jobs[0].JobId).Name} for this code."
                : document.Jobs.Count == PartyCooldownCatalog.Jobs.Count ? "This is an all-jobs code. Use Import above the job list."
                : targetJobId is null ? "This code doesn't include every job. Ask for an all-jobs export." : "This button needs a code for this job only.";
            error = new(CooldownImportProblem.WrongScope, instruction);
            return false;
        }

        // Stage every job, including copies of legacy local rows, before replacing any saved settings.
        var staged = new List<(uint Job, List<uint> Actions, PartyCooldownRows? Rows)>();
        foreach (var shared in document.Jobs)
        {
            var actions = catalog.NormalizeSelection(shared.JobId, shared.SelectedActionIds);
            if (actions.Count != shared.SelectedActionIds.Count)
            {
                var name = PartyCooldownCatalog.Jobs.First(job => job.Id == shared.JobId).Name;
                error = new(CooldownImportProblem.UnavailableAbility, $"{name}: the code contains unavailable or repeated cooldowns.");
                return false;
            }
            PartyCooldownRows? rows = null;
            if (document.Version == Version)
            {
                rows = new()
                {
                    Enabled = shared.RowsEnabled,
                    Rows = shared.Rows.Select(row => row.Select(PartyCooldownSelectionPolicy.CanonicalAction).ToList()).ToList(),
                };
            }
            else
            {
                actions = MergeSelectionPreservingLocalOrder(
                    selections.TryGetValue(shared.JobId, out var local) ? local : catalog.DefaultSelection(shared.JobId), actions);
                if (rowLayouts.TryGetValue(shared.JobId, out var existing))
                {
                    rows = new() { Enabled = existing.Enabled, Rows = existing.Rows.Select(row => row.ToList()).ToList() };
                    rows.Normalize(actions);
                }
            }
            staged.Add((shared.JobId, rows?.Enabled == true ? rows.Flatten() : actions, rows));
        }
        foreach (var item in staged)
        {
            selections[item.Job] = item.Actions;
            if (item.Rows is not null)
                rowLayouts[item.Job] = item.Rows;
        }
        layout = document.Layout;
        return true;
    }

    private static bool ValidatePlacement(PartyCooldownLoadoutSharePlacement placement, string side, out CooldownImportError error)
    {
        return InRange(side, "icon size", placement.IconSize, PartyCooldownLayout.MinimumIconSize, PartyCooldownLayout.MaximumIconSize, out error)
            && InRange(side, "spacing", placement.Spacing, 0, PartyCooldownLayout.MaximumGap, out error)
            && InRange(side, "horizontal offset", placement.OffsetX, -PartyCooldownLayout.MaximumOffset, PartyCooldownLayout.MaximumOffset, out error)
            && InRange(side, "vertical offset", placement.OffsetY, -PartyCooldownLayout.MaximumOffset, PartyCooldownLayout.MaximumOffset, out error);
    }

    private static bool InRange(string side, string setting, float value, float min, float max, out CooldownImportError error, string unit = " px")
    {
        error = default;
        if (float.IsFinite(value) && value >= min && value <= max)
            return true;
        error = new(CooldownImportProblem.InvalidPlacement, $"{side} {setting}: {value:G}{unit}. Allowed: {min} to {max}{unit}.");
        return false;
    }
}
