using BetterDeaths.DamageParsing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterDeaths;

internal sealed record RecordedDamageEncounter(
    long EncounterNumber,
    DateTime CapturedAtUtc,
    uint TerritoryId,
    string TerritoryName,
    DamageEncounterSnapshot Snapshot);

internal sealed record DamageMeterDiagnosticEncounter(
    DateTime CapturedAtUtc,
    uint TerritoryId,
    string TerritoryName,
    DamageEncounterSnapshot Snapshot);

public sealed partial class Plugin
{
    private const string RecordedDamageEncounterFileName = "recorded-damage-encounters.json";
    private const int RecordedDamageEncounterSchemaVersion = 1;
    private const int MaxRecordedDamageEncounters = 40;
    private const string DamageMeterDiagnosticEncounterFileName = "damage-meter-latest-encounter.json";
    private const int DamageMeterDiagnosticEncounterSchemaVersion = 1;

    private sealed record RecordedDamageEncounterFile(
        int SchemaVersion,
        List<RecordedDamageEncounter> Encounters);

    private sealed record DamageMeterDiagnosticEncounterFile(
        int SchemaVersion,
        DamageMeterDiagnosticEncounter Encounter);

    private static readonly JsonSerializerOptions RecordedDamageEncounterJsonOptions = new()
    {
        WriteIndented = false,
    };
    private DamageMeterDiagnosticEncounter? loadedDamageMeterDiagnosticEncounter;
    private DamageMeterDiagnosticEncounter? latestCompletedDamageMeterDiagnosticEncounter;
    private bool attemptedDamageMeterDiagnosticEncounterLoad;
    private long damageEncounterHistoryGeneration;
    private long damageEncounterDiagnosticGeneration;
    private readonly OrderedBackgroundWorkQueue damageEncounterWork = new(
        error => Log.Warning(error, "Could not complete Better Deaths damage encounter background work."));
    private readonly ConcurrentQueue<CompletedDamageEncounter> completedDamageEncounters = new();

    private sealed record DamageEncounterCompletionContext(
        DateTime EndedAtUtc, string Reason, float PullElapsedSeconds,
        uint TerritoryId, string TerritoryName,
        long HistoryGeneration, long DiagnosticGeneration,
        bool ExportDiagnostic, bool TraceSummary,
        string HistoryPath, string DiagnosticPath);

    private sealed record CompletedDamageEncounter(
        DamageParsingModule.DetachedEncounter Detached,
        DamageEncounterSnapshot Snapshot,
        DamageEncounterCompletionContext Context,
        bool DiagnosticSaved);

    private static string RecordedDamageEncounterPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedDamageEncounterFileName);

    private static string DamageMeterDiagnosticEncounterPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, DamageMeterDiagnosticEncounterFileName);

    internal string DamageMeterDiagnosticEncounterFilePath => DamageMeterDiagnosticEncounterPath;

    internal long DamageMeterDiagnosticEncounterFileSizeBytes
    {
        get
        {
            try
            {
                return File.Exists(DamageMeterDiagnosticEncounterPath)
                    ? new FileInfo(DamageMeterDiagnosticEncounterPath).Length
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal void ClearRecordedDamageEncounters()
    {
        damageEncounterHistoryGeneration++;
        lock (recordedDamageEncounterLock)
        {
            recordedDamageEncounters = [];
            nextRecordedDamageEncounterNumber = 1;
        }

        var path = RecordedDamageEncounterPath;
        damageEncounterWork.Enqueue(() =>
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        });
    }

    internal void ClearDamageMeterDiagnosticEncounter()
    {
        damageEncounterDiagnosticGeneration++;
        loadedDamageMeterDiagnosticEncounter = null;
        attemptedDamageMeterDiagnosticEncounterLoad = true;
        var path = DamageMeterDiagnosticEncounterPath;
        damageEncounterWork.Enqueue(() =>
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        });
    }

    internal DamageMeterDiagnosticEncounter? GetLatestDamageMeterDiagnosticEncounter()
    {
        if (latestCompletedDamageMeterDiagnosticEncounter is { } latest)
        {
            return latest;
        }

        if (attemptedDamageMeterDiagnosticEncounterLoad)
        {
            return loadedDamageMeterDiagnosticEncounter;
        }

        attemptedDamageMeterDiagnosticEncounterLoad = true;

        try
        {
            if (!File.Exists(DamageMeterDiagnosticEncounterPath))
            {
                return null;
            }

            var file = JsonSerializer.Deserialize<DamageMeterDiagnosticEncounterFile>(
                File.ReadAllText(DamageMeterDiagnosticEncounterPath),
                RecordedDamageEncounterJsonOptions);
            loadedDamageMeterDiagnosticEncounter = file?.SchemaVersion == DamageMeterDiagnosticEncounterSchemaVersion
                ? file.Encounter
                : null;
            return loadedDamageMeterDiagnosticEncounter;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the Better Deaths damage-meter diagnostic encounter.");
            return null;
        }
    }

    private void LoadRecordedDamageEncounters()
    {
        try
        {
            if (!File.Exists(RecordedDamageEncounterPath))
            {
                return;
            }

            var json = File.ReadAllText(RecordedDamageEncounterPath);
            var file = JsonSerializer.Deserialize<RecordedDamageEncounterFile>(
                json,
                RecordedDamageEncounterJsonOptions);
            if (file is null || file.SchemaVersion != RecordedDamageEncounterSchemaVersion)
            {
                return;
            }

            var loaded = file.Encounters
                .Where(encounter =>
                    encounter.EncounterNumber > 0 &&
                    encounter.Snapshot is not null &&
                    encounter.Snapshot.TotalDamage > 0)
                .OrderBy(encounter => encounter.EncounterNumber)
                .TakeLast(MaxRecordedDamageEncounters)
                .ToList();
            lock (recordedDamageEncounterLock)
            {
                recordedDamageEncounters = loaded;
                nextRecordedDamageEncounterNumber = loaded.Count == 0
                    ? 1
                    : loaded[^1].EncounterNumber + 1;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load Better Deaths damage encounter history.");
        }
    }

    private void PublishCompletedDamageEncounters()
    {
        while (completedDamageEncounters.TryDequeue(out var completed))
        {
            var encounter = completed.Snapshot;
            var context = completed.Context;
            damageParsingModule.PublishCompletedEncounter(completed.Detached, encounter);
            latestCompletedDamageMeterDiagnosticEncounter = new DamageMeterDiagnosticEncounter(
                encounter.EndedAtUtc ?? encounter.SnapshotAtUtc,
                context.TerritoryId, context.TerritoryName, encounter);
            if (completed.DiagnosticSaved && context.DiagnosticGeneration == damageEncounterDiagnosticGeneration)
            {
                loadedDamageMeterDiagnosticEncounter = latestCompletedDamageMeterDiagnosticEncounter;
                attemptedDamageMeterDiagnosticEncounterLoad = true;
            }

            if (context.HistoryGeneration == damageEncounterHistoryGeneration)
                RecordCompletedDamageEncounter(encounter, context);
        }
    }

    private void RecordCompletedDamageEncounter(DamageEncounterSnapshot encounter, DamageEncounterCompletionContext context)
    {
        if (encounter.TotalDamage == 0)
        {
            return;
        }

        // Keep compact source, target, and attribution aggregates for later comparison without
        // retaining the full event stream for every encounter.
        var storedSnapshot = encounter with
        {
            Events = [],
        };

        IReadOnlyList<RecordedDamageEncounter> snapshot;
        lock (recordedDamageEncounterLock)
        {
            var updated = recordedDamageEncounters.ToList();
            updated.Add(new RecordedDamageEncounter(
                nextRecordedDamageEncounterNumber++,
                encounter.EndedAtUtc ?? encounter.SnapshotAtUtc,
                context.TerritoryId,
                context.TerritoryName,
                storedSnapshot));
            if (updated.Count > MaxRecordedDamageEncounters)
            {
                updated.RemoveRange(0, updated.Count - MaxRecordedDamageEncounters);
            }

            recordedDamageEncounters = updated;
            snapshot = updated;
        }

        damageEncounterWork.Enqueue(() => SaveRecordedDamageEncounters(snapshot, context.HistoryPath));
    }

    private static bool SaveDamageMeterDiagnosticEncounter(DamageEncounterSnapshot encounter, DamageEncounterCompletionContext context)
    {
        try
        {
            var diagnostic = new DamageMeterDiagnosticEncounter(
                encounter.EndedAtUtc ?? encounter.SnapshotAtUtc,
                context.TerritoryId,
                context.TerritoryName,
                encounter);
            var file = new DamageMeterDiagnosticEncounterFile(
                DamageMeterDiagnosticEncounterSchemaVersion,
                diagnostic);
            DamageEncounterFileWriter.Write(context.DiagnosticPath, file, RecordedDamageEncounterJsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save the Better Deaths damage-meter diagnostic encounter.");
            return false;
        }
    }

    private static void SaveRecordedDamageEncounters(IReadOnlyList<RecordedDamageEncounter> encounters, string path)
    {
        try
        {
            var file = new RecordedDamageEncounterFile(
                RecordedDamageEncounterSchemaVersion,
                encounters.ToList());
            DamageEncounterFileWriter.Write(path, file, RecordedDamageEncounterJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save Better Deaths damage encounter history.");
        }
    }
}
