using BetterDeaths.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private sealed record RecordedPullHistoryFile(int SchemaVersion, List<PullDeathSnapshot> Pulls);

    private sealed record RecordedPullIndexFile(int SchemaVersion, List<RecordedPullIndexEntry> Pulls);

    private sealed record RecordedPullIndexEntry(
        DateTime CapturedAtUtc,
        string Reason,
        uint TerritoryId,
        string TerritoryName,
        float PullElapsedSeconds,
        int DeathCount,
        long PullNumber,
        string DetailFileName)
    {
        public string CapturedPluginVersion { get; init; } = string.Empty;

        public string PullGroupId { get; init; } = string.Empty;

        public int PullGroupColorIndex { get; init; } = -1;
    }

    private sealed class RecordedPullState
    {
        public RecordedPullState(
            RecordedPullSummary summary,
            string detailFileName,
            PullDeathSnapshot? detail,
            bool detailDirty)
        {
            Summary = summary;
            DetailFileName = detailFileName;
            Detail = detail;
            DetailDirty = detailDirty;
        }

        public RecordedPullSummary Summary { get; set; }

        public string DetailFileName { get; set; }

        public PullDeathSnapshot? Detail { get; set; }

        public bool DetailDirty { get; set; }
    }

    public void ClearRecordedPulls()
    {
        WaitForRecordedPullHistoryLoadForMutation();
        lock (recordedPullLock)
        {
            recordedPulls.Clear();
            recordedPullSummaries = [];
            nextRecordedPullNumber = 1;
            recordedPullStorageDirty = false;
        }

        currentPullRecordedPullNumber = 0;
        DeleteRecordedPullHistoryFiles();
    }

    private void TrimRecordedPullsLocked()
    {
        while (recordedPulls.Count > Configuration.MaxRecordedPulls)
        {
            recordedPulls.RemoveAt(0);
            recordedPullStorageDirty = true;
        }
    }

    private void UpdateRecordedPullSummariesLocked()
    {
        recordedPullSummaries = recordedPulls
            .Select(state => state.Summary)
            .ToList();
    }

    private long GetNextRecordedPullNumber()
    {
        lock (recordedPullLock)
        {
            var next = GetNextRecordedPullNumberLocked();
            nextRecordedPullNumber = next + 1;
            return next;
        }
    }

    private long GetNextRecordedPullNumberLocked()
    {
        var next = Math.Max(1, nextRecordedPullNumber);
        if (recordedPulls.Count > 0)
        {
            next = Math.Max(next, recordedPulls.Max(pull => pull.Summary.PullNumber) + 1);
        }

        return next;
    }

    private static string RecordedPullHistoryPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullHistoryFileName);

    private static string RecordedPullIndexPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullIndexFileName);

    private static string RecordedPullDetailsDirectoryPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullDetailsDirectoryName);

    private static string DebugCaptureFileFullPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, DebugCaptureFileName);

    private static string DebugCaptureTempFilePath =>
        DebugCaptureFileFullPath + ".tmp";

    private static string RecordedPullHistoryTempPath =>
        RecordedPullHistoryPath + ".tmp";

    private static string RecordedPullHistoryBackupPath =>
        RecordedPullHistoryPath + ".bak";

    private static string RecordedPullIndexTempPath =>
        RecordedPullIndexPath + ".tmp";

    private static string RecordedPullIndexBackupPath =>
        RecordedPullIndexPath + ".bak";

    private void BeginLoadRecordedPullHistory()
    {
        recordedPullHistoryLoading = true;
        recordedPullHistoryLoadError = null;
        recordedPullHistoryLoadCts = new CancellationTokenSource();
        var token = recordedPullHistoryLoadCts.Token;
        recordedPullHistoryLoadTask = Task.Run(() => LoadRecordedPullHistoryInBackground(token), token);
    }

    private void LoadRecordedPullHistoryInBackground(CancellationToken cancellationToken)
    {
        try
        {
            var states = LoadRecordedPullStates(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ApplyLoadedRecordedPullStates(states);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            recordedPullHistoryLoadError = ex.Message;
            Log.Warning(ex, "Could not load Better Deaths recorded pull history.");
        }
        finally
        {
            recordedPullHistoryLoading = false;
        }
    }

    private List<RecordedPullState> LoadRecordedPullStates(CancellationToken cancellationToken)
    {
        if (TryReadRecordedPullIndexFile(RecordedPullIndexPath) is { Count: > 0 } indexedStates)
        {
            return NormalizeAndMigrateRecordedPullStates(indexedStates);
        }

        if (TryReadRecordedPullIndexFile(RecordedPullIndexTempPath) is { Count: > 0 } tempIndexedStates)
        {
            return NormalizeAndMigrateRecordedPullStates(tempIndexedStates);
        }

        if (TryReadRecordedPullIndexFile(RecordedPullIndexBackupPath) is { Count: > 0 } backupIndexedStates)
        {
            return NormalizeAndMigrateRecordedPullStates(backupIndexedStates);
        }

        foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
        {
            if (TryReadRecordedPullIndexFile(backupPath) is { Count: > 0 } rollingIndexedStates)
            {
                return NormalizeAndMigrateRecordedPullStates(rollingIndexedStates);
            }
        }

        var loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryPath);

        if (loadedPulls is null && File.Exists(RecordedPullHistoryTempPath))
        {
            loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryTempPath);
        }

        if (loadedPulls is null && File.Exists(RecordedPullHistoryBackupPath))
        {
            loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryBackupPath);
        }

        if (loadedPulls is null)
        {
            foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
            {
                loadedPulls = TryReadRecordedPullHistoryFile(backupPath);
                if (loadedPulls is not null)
                {
                    break;
                }
            }
        }

        if (loadedPulls is null)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPulls = NormalizeRecordedPullNumbers(loadedPulls
            .Where(pull => pull is { Deaths.Count: > 0 })
            .OrderBy(pull => pull.CapturedAtUtc))
            .TakeLast(Configuration.MaxRecordedPulls)
            .ToList();
        var migratedStates = normalizedPulls
            .Select(pull => CreateRecordedPullState(pull, detailDirty: true))
            .ToList();
        try
        {
            WriteRecordedPullStorageSnapshot(migratedStates, createBackup: false);
            foreach (var state in migratedStates)
            {
                state.DetailDirty = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not migrate Better Deaths recorded pulls into split storage.");
        }

        return migratedStates;
    }

    private static List<RecordedPullState> NormalizeAndMigrateRecordedPullStates(IEnumerable<RecordedPullState> states)
    {
        var normalized = NormalizeRecordedPullStates(states);
        if (normalized.Count == 0)
        {
            return normalized;
        }

        try
        {
            if (MigrateRecordedPullDetailsToCompressed(normalized))
            {
                WriteRecordedPullStorageSnapshot(normalized, createBackup: false);
                foreach (var state in normalized)
                {
                    state.DetailDirty = false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not migrate Better Deaths recorded pull details to compressed storage.");
        }

        return normalized;
    }

    private void ApplyLoadedRecordedPullStates(List<RecordedPullState> states)
    {
        lock (recordedPullLock)
        {
            recordedPulls.Clear();
            recordedPulls.AddRange(states);
            TrimRecordedPullsLocked();
            UpdateRecordedPullSummariesLocked();
            nextRecordedPullNumber = GetNextRecordedPullNumberLocked();
            recordedPullStorageDirty = false;
        }
    }

    private static List<RecordedPullState> NormalizeRecordedPullStates(IEnumerable<RecordedPullState> states)
    {
        var normalized = new List<RecordedPullState>();
        var usedPullNumbers = new HashSet<long>();
        var nextPullNumber = 1L;

        foreach (var state in states
                     .Where(state => state.Summary.DeathCount > 0)
                     .OrderBy(state => state.Summary.CapturedAtUtc))
        {
            var pullNumber = state.Summary.PullNumber;
            if (pullNumber <= 0 || !usedPullNumbers.Add(pullNumber))
            {
                while (usedPullNumbers.Contains(nextPullNumber))
                {
                    nextPullNumber++;
                }

                pullNumber = nextPullNumber;
                usedPullNumbers.Add(pullNumber);
            }

            nextPullNumber = Math.Max(nextPullNumber, pullNumber + 1);
            var summary = state.Summary with { PullNumber = pullNumber };
            normalized.Add(new RecordedPullState(summary, state.DetailFileName, state.Detail, state.DetailDirty));
        }

        return normalized;
    }

    private static List<PullDeathSnapshot> NormalizeRecordedPullNumbers(IEnumerable<PullDeathSnapshot> pulls)
    {
        var normalized = new List<PullDeathSnapshot>();
        var usedPullNumbers = new HashSet<long>();
        var nextPullNumber = 1L;

        foreach (var pull in pulls)
        {
            var pullNumber = pull.PullNumber;
            if (pullNumber <= 0 || !usedPullNumbers.Add(pullNumber))
            {
                while (usedPullNumbers.Contains(nextPullNumber))
                {
                    nextPullNumber++;
                }

                pullNumber = nextPullNumber;
                usedPullNumbers.Add(pullNumber);
            }

            nextPullNumber = Math.Max(nextPullNumber, pullNumber + 1);
            normalized.Add(pull with { PullNumber = pullNumber });
        }

        return normalized;
    }

    private List<PullDeathSnapshot>? TryReadRecordedPullHistoryFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : DeserializeRecordedPullHistory(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not read Better Deaths recorded pull history at {path}.");
            return null;
        }
    }

    private static List<PullDeathSnapshot>? DeserializeRecordedPullHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<PullDeathSnapshot>>(json, RecordedPullHistoryJsonOptions),
            JsonValueKind.Object when document.RootElement.TryGetProperty(nameof(RecordedPullHistoryFile.Pulls), out _) =>
                JsonSerializer.Deserialize<RecordedPullHistoryFile>(json, RecordedPullHistoryJsonOptions)?.Pulls,
            _ => null,
        };
    }

    private List<RecordedPullState>? TryReadRecordedPullIndexFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var index = JsonSerializer.Deserialize<RecordedPullIndexFile>(json, RecordedPullHistoryJsonOptions);
            return index?.Pulls
                .Where(entry => entry.DeathCount > 0 && !string.IsNullOrWhiteSpace(entry.DetailFileName))
                .Select(entry =>
                {
                    var summary = new RecordedPullSummary(
                        entry.CapturedAtUtc,
                        entry.Reason,
                        entry.TerritoryId,
                        entry.TerritoryName,
                        entry.PullElapsedSeconds,
                        entry.DeathCount)
                    {
                        PullNumber = entry.PullNumber,
                        CapturedPluginVersion = entry.CapturedPluginVersion ?? string.Empty,
                        PullGroupId = entry.PullGroupId ?? string.Empty,
                        PullGroupColorIndex = entry.PullGroupColorIndex,
                    };
                    return new RecordedPullState(summary, entry.DetailFileName, null, detailDirty: false);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not read Better Deaths recorded pull index at {path}.");
            return null;
        }
    }

    private void SaveRecordedPullHistory()
    {
        if (!recordedPullStorageDirty)
        {
            return;
        }

        WaitForRecordedPullHistoryLoadForMutation();
        List<RecordedPullState> snapshot;
        lock (recordedPullLock)
        {
            if (!recordedPullStorageDirty)
            {
                return;
            }

            snapshot = recordedPulls.ToList();
        }

        try
        {
            WriteRecordedPullStorageSnapshot(snapshot, createBackup: true);
            lock (recordedPullLock)
            {
                foreach (var state in recordedPulls)
                {
                    state.DetailDirty = false;
                }

                recordedPullStorageDirty = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save Better Deaths recorded pull history.");
        }
    }

    private static void WriteRecordedPullStorageSnapshot(
        IReadOnlyList<RecordedPullState> states,
        bool createBackup)
    {
        Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
        Directory.CreateDirectory(RecordedPullDetailsDirectoryPath);

        foreach (var state in states)
        {
            if (state.Detail is null)
            {
                continue;
            }

            state.DetailFileName = GetCompressedRecordedPullDetailFileName(state.DetailFileName);
            var detailPath = GetRecordedPullDetailPath(state.DetailFileName);
            if (!state.DetailDirty && File.Exists(detailPath))
            {
                continue;
            }

            WriteRecordedPullDetailFile(state.Detail, detailPath);
        }

        var index = new RecordedPullIndexFile(
            RecordedPullIndexSchemaVersion,
            states
                .Select(state => new RecordedPullIndexEntry(
                    state.Summary.CapturedAtUtc,
                    state.Summary.Reason,
                    state.Summary.TerritoryId,
                    state.Summary.TerritoryName,
                    state.Summary.PullElapsedSeconds,
                    state.Summary.DeathCount,
                    state.Summary.PullNumber,
                    state.DetailFileName)
                {
                    CapturedPluginVersion = state.Summary.CapturedPluginVersion ?? string.Empty,
                    PullGroupId = state.Summary.PullGroupId ?? string.Empty,
                    PullGroupColorIndex = state.Summary.PullGroupColorIndex,
                })
                .ToList());
        var indexJson = JsonSerializer.Serialize(index, RecordedPullHistoryJsonOptions);
        if (createBackup)
        {
            CreateRecordedPullIndexRollingBackup();
        }

        File.WriteAllText(RecordedPullIndexTempPath, indexJson);
        if (File.Exists(RecordedPullIndexPath))
        {
            File.Replace(RecordedPullIndexTempPath, RecordedPullIndexPath, RecordedPullIndexBackupPath, true);
        }
        else
        {
            File.Move(RecordedPullIndexTempPath, RecordedPullIndexPath, true);
        }

        PruneRecordedPullIndexRollingBackups();
        PruneOrphanRecordedPullDetailFiles(states);
    }

    private void WaitForRecordedPullHistoryLoadForMutation()
    {
        var task = recordedPullHistoryLoadTask;
        if (task is null || task.IsCompleted)
        {
            return;
        }

        try
        {
            if (task.Wait(TimeSpan.FromSeconds(10)))
            {
                return;
            }

            recordedPullHistoryLoadCts?.Cancel();
            Log.Warning("Better Deaths recorded pull history load timed out before mutation; canceling background load.");
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            Log.Warning(ex, "Better Deaths recorded pull history load did not finish before mutation.");
        }
    }

    public PullDeathSnapshot? GetRecordedPullDetails(RecordedPullSummary summary)
    {
        RecordedPullState? state;
        lock (recordedPullLock)
        {
            state = FindRecordedPullStateLocked(summary);
            if (state?.Detail is not null)
            {
                return state.Detail;
            }
        }

        if (state is null)
        {
            return null;
        }

        var detail = TryReadRecordedPullDetailFile(GetRecordedPullDetailPath(state.DetailFileName), out var resolvedDetailPath);
        if (detail is null)
        {
            return null;
        }

        lock (recordedPullLock)
        {
            state = FindRecordedPullStateLocked(summary);
            if (state is null)
            {
                return detail;
            }

            state.Detail = detail;
            state.DetailDirty = false;
            if (!string.IsNullOrWhiteSpace(resolvedDetailPath))
            {
                state.DetailFileName = Path.GetFileName(resolvedDetailPath);
            }

            return state.Detail;
        }
    }

    public PullDeathSnapshot? GetLoadedRecordedPullDetails(RecordedPullSummary summary)
    {
        lock (recordedPullLock)
        {
            return FindRecordedPullStateLocked(summary)?.Detail;
        }
    }

    private List<PullDeathSnapshot> GetLoadedRecordedPullDetails()
    {
        lock (recordedPullLock)
        {
            return recordedPulls
                .Select(state => state.Detail)
                .Where(detail => detail is not null)
                .Cast<PullDeathSnapshot>()
                .ToList();
        }
    }

    private List<PullDeathSnapshot> GetRecordedPullDetailsForSearch()
    {
        return RecordedPulls
            .Reverse()
            .Select(GetRecordedPullDetails)
            .Where(detail => detail is not null)
            .Cast<PullDeathSnapshot>()
            .ToList();
    }

    private RecordedPullState? FindRecordedPullStateLocked(RecordedPullSummary summary)
    {
        return recordedPulls.FirstOrDefault(state =>
            state.Summary.PullNumber == summary.PullNumber &&
            state.Summary.CapturedAtUtc.Ticks == summary.CapturedAtUtc.Ticks);
    }

    private static PullDeathSnapshot? TryReadRecordedPullDetailFile(string path)
    {
        return TryReadRecordedPullDetailFile(path, out _);
    }

    private static PullDeathSnapshot? TryReadRecordedPullDetailFile(string path, out string? resolvedPath)
    {
        resolvedPath = null;
        Exception? lastException = null;
        foreach (var candidatePath in GetRecordedPullDetailReadCandidatePaths(path))
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            try
            {
                using var stream = OpenRecordedPullDetailReadStream(candidatePath);
                var detail = JsonSerializer.Deserialize<PullDeathSnapshot>(stream, RecordedPullHistoryJsonOptions);
                if (detail is null)
                {
                    continue;
                }

                resolvedPath = candidatePath;
                return detail;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (lastException is not null)
        {
            Log.Warning(lastException, $"Could not read Better Deaths recorded pull details at {path}.");
        }

        return null;
    }

    private static void WriteRecordedPullDetailFile(PullDeathSnapshot detail, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RecordedPullDetailsDirectoryPath);
        var tempPath = BuildTempFilePath(path);
        TryDeleteFile(tempPath);
        try
        {
            if (IsCompressedRecordedPullDetailFileName(path))
            {
                using var fileStream = File.Create(tempPath);
                using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);
                JsonSerializer.Serialize(gzipStream, detail, RecordedPullHistoryJsonOptions);
            }
            else
            {
                var json = JsonSerializer.Serialize(detail, RecordedPullHistoryJsonOptions);
                File.WriteAllText(tempPath, json, Encoding.UTF8);
            }

            ReplaceWithTempFile(tempPath, path);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static RecordedPullState CreateRecordedPullState(PullDeathSnapshot pull, bool detailDirty)
    {
        var summary = new RecordedPullSummary(
            pull.CapturedAtUtc,
            pull.Reason,
            pull.TerritoryId,
            pull.TerritoryName,
            pull.PullElapsedSeconds,
            pull.Deaths.Count)
        {
            PullNumber = pull.PullNumber,
            CapturedPluginVersion = pull.CapturedPluginVersion ?? string.Empty,
            PullGroupId = pull.PullGroupId ?? string.Empty,
            PullGroupColorIndex = pull.PullGroupColorIndex,
        };
        return new RecordedPullState(summary, BuildRecordedPullDetailFileName(pull), pull, detailDirty);
    }

    private static string BuildRecordedPullDetailFileName(PullDeathSnapshot pull)
    {
        var pullNumber = Math.Max(0, pull.PullNumber);
        return $"pull-{pullNumber:D6}-{pull.CapturedAtUtc.Ticks}{RecordedPullDetailCompressedExtension}";
    }

    private static string GetRecordedPullDetailPath(string fileName)
    {
        return Path.Combine(RecordedPullDetailsDirectoryPath, Path.GetFileName(fileName));
    }

    private static bool MigrateRecordedPullDetailsToCompressed(IReadOnlyList<RecordedPullState> states)
    {
        if (!Directory.Exists(RecordedPullDetailsDirectoryPath))
        {
            return false;
        }

        var changed = false;
        foreach (var state in states)
        {
            if (IsCompressedRecordedPullDetailFileName(state.DetailFileName))
            {
                continue;
            }

            var jsonPath = GetRecordedPullDetailPath(state.DetailFileName);
            var compressedFileName = GetCompressedRecordedPullDetailFileName(state.DetailFileName);
            var compressedPath = GetRecordedPullDetailPath(compressedFileName);
            if (File.Exists(compressedPath))
            {
                if (ValidateCompressedRecordedPullDetailFile(compressedPath))
                {
                    state.DetailFileName = compressedFileName;
                    changed = true;
                    continue;
                }

                TryDeleteFile(compressedPath);
            }

            if (!File.Exists(jsonPath))
            {
                continue;
            }

            try
            {
                CompressRecordedPullDetailFile(jsonPath, compressedPath);
                if (!ValidateCompressedRecordedPullDetailFile(compressedPath))
                {
                    TryDeleteFile(compressedPath);
                    continue;
                }

                state.DetailFileName = compressedFileName;
                changed = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Could not compress Better Deaths recorded pull detail at {jsonPath}.");
            }
        }

        return changed;
    }

    private static void CompressRecordedPullDetailFile(string sourcePath, string compressedPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(compressedPath) ?? RecordedPullDetailsDirectoryPath);
        var tempPath = compressedPath + RecordedPullDetailMigrationTempSuffix;
        TryDeleteFile(tempPath);
        try
        {
            using (var sourceStream = File.OpenRead(sourcePath))
            using (var targetStream = File.Create(tempPath))
            using (var gzipStream = new GZipStream(targetStream, CompressionLevel.SmallestSize))
            {
                sourceStream.CopyTo(gzipStream);
            }

            ReplaceWithTempFile(tempPath, compressedPath);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static bool ValidateCompressedRecordedPullDetailFile(string path)
    {
        try
        {
            using var stream = OpenRecordedPullDetailReadStream(path);
            stream.CopyTo(Stream.Null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Stream OpenRecordedPullDetailReadStream(string path)
    {
        var fileStream = File.OpenRead(path);
        if (IsCompressedRecordedPullDetailFileName(path) || HasGzipMagicBytes(fileStream))
        {
            fileStream.Position = 0;
            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        fileStream.Position = 0;
        return fileStream;
    }

    private static bool HasGzipMagicBytes(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 2)
        {
            return false;
        }

        var first = stream.ReadByte();
        var second = stream.ReadByte();
        stream.Position = 0;
        return first == 0x1F && second == 0x8B;
    }

    private static IEnumerable<string> GetRecordedPullDetailReadCandidatePaths(string path)
    {
        yield return path;

        var alternatePath = GetAlternateRecordedPullDetailPath(path);
        if (!string.Equals(alternatePath, path, StringComparison.OrdinalIgnoreCase))
        {
            yield return alternatePath;
        }
    }

    private static string GetAlternateRecordedPullDetailPath(string path)
    {
        return IsCompressedRecordedPullDetailFileName(path)
            ? path[..^3]
            : path + ".gz";
    }

    private static string GetCompressedRecordedPullDetailFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (IsCompressedRecordedPullDetailFileName(safeName))
        {
            return safeName;
        }

        return safeName.EndsWith(RecordedPullDetailJsonExtension, StringComparison.OrdinalIgnoreCase)
            ? safeName + ".gz"
            : safeName + RecordedPullDetailCompressedExtension;
    }

    private static bool IsCompressedRecordedPullDetailFileName(string pathOrFileName)
    {
        return pathOrFileName.EndsWith(RecordedPullDetailCompressedExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTempFilePath(string path)
    {
        return path + ".tmp";
    }

    private static void ReplaceWithTempFile(string tempPath, string path)
    {
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null, true);
        }
        else
        {
            File.Move(tempPath, path, true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void DeleteRecordedPullHistoryFiles()
    {
        try
        {
            foreach (var path in GetRecordedPullHistoryFilesForDelete())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Could not delete Better Deaths recorded pull history file at {path}.");
                }
            }

            if (Directory.Exists(RecordedPullDetailsDirectoryPath))
            {
                Directory.Delete(RecordedPullDetailsDirectoryPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete Better Deaths recorded pull history files.");
        }
    }

    private static IEnumerable<string> GetRecordedPullHistoryFilesForDelete()
    {
        yield return RecordedPullHistoryPath;
        yield return RecordedPullHistoryTempPath;
        yield return RecordedPullHistoryBackupPath;
        yield return RecordedPullIndexPath;
        yield return RecordedPullIndexTempPath;
        yield return RecordedPullIndexBackupPath;

        foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
        {
            yield return backupPath;
        }

        foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
        {
            yield return backupPath;
        }
    }

    private static void CreateRecordedPullIndexRollingBackup()
    {
        try
        {
            if (!File.Exists(RecordedPullIndexPath))
            {
                return;
            }

            var backupPath = Path.Combine(
                PluginInterface.ConfigDirectory.FullName,
                $"recorded-pulls.index.backup.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
            File.Copy(RecordedPullIndexPath, backupPath, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create Better Deaths recorded pull index rolling backup.");
        }
    }

    private static IEnumerable<string> GetRecordedPullHistoryRollingBackupPaths()
    {
        try
        {
            if (!Directory.Exists(PluginInterface.ConfigDirectory.FullName))
            {
                return [];
            }

            return Directory.EnumerateFiles(
                    PluginInterface.ConfigDirectory.FullName,
                    RecordedPullHistoryRollingBackupSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not list Better Deaths recorded pull history rolling backups.");
            return [];
        }
    }

    private static IEnumerable<string> GetRecordedPullIndexRollingBackupPaths()
    {
        try
        {
            if (!Directory.Exists(PluginInterface.ConfigDirectory.FullName))
            {
                return [];
            }

            return Directory.EnumerateFiles(
                    PluginInterface.ConfigDirectory.FullName,
                    RecordedPullIndexRollingBackupSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not list Better Deaths recorded pull index rolling backups.");
            return [];
        }
    }

    private static void PruneRecordedPullIndexRollingBackups()
    {
        try
        {
            foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths().Skip(RecordedPullIndexRollingBackupCount))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not prune Better Deaths recorded pull index rolling backups.");
        }
    }

    private static void PruneOrphanRecordedPullDetailFiles(IReadOnlyList<RecordedPullState> states)
    {
        try
        {
            if (!Directory.Exists(RecordedPullDetailsDirectoryPath))
            {
                return;
            }

            var retainedNames = states
                .Select(state => Path.GetFileName(state.DetailFileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in EnumerateRecordedPullDetailStorageFiles())
            {
                if (!retainedNames.Contains(Path.GetFileName(path)))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not prune Better Deaths recorded pull detail files.");
        }
    }

    private static long GetRecordedPullStorageSizeBytes()
    {
        try
        {
            var total = GetFileSizeBytes(RecordedPullHistoryPath) +
                GetFileSizeBytes(RecordedPullHistoryTempPath) +
                GetFileSizeBytes(RecordedPullHistoryBackupPath) +
                GetFileSizeBytes(RecordedPullIndexPath) +
                GetFileSizeBytes(RecordedPullIndexTempPath) +
                GetFileSizeBytes(RecordedPullIndexBackupPath);

            foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
            {
                total += GetFileSizeBytes(backupPath);
            }

            foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
            {
                total += GetFileSizeBytes(backupPath);
            }

            total += GetDirectorySizeBytes(RecordedPullDetailsDirectoryPath);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetRecordedPullDetailFileCount()
    {
        try
        {
            return Directory.Exists(RecordedPullDetailsDirectoryPath)
                ? EnumerateRecordedPullDetailFiles().Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> EnumerateRecordedPullDetailFiles()
    {
        if (!Directory.Exists(RecordedPullDetailsDirectoryPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(RecordedPullDetailsDirectoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsRecordedPullDetailFileName(Path.GetFileName(path)))
            .ToList();
    }

    private static IEnumerable<string> EnumerateRecordedPullDetailStorageFiles()
    {
        if (!Directory.Exists(RecordedPullDetailsDirectoryPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(RecordedPullDetailsDirectoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return IsRecordedPullDetailFileName(fileName) ||
                    fileName.EndsWith(RecordedPullDetailMigrationTempSuffix, StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static bool IsRecordedPullDetailFileName(string fileName)
    {
        return fileName.EndsWith(RecordedPullDetailJsonExtension, StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(RecordedPullDetailCompressedExtension, StringComparison.OrdinalIgnoreCase);
    }
}
