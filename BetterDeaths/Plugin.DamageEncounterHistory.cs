using BetterDeaths.DamageParsing;
using System;
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

public sealed partial class Plugin
{
    private const string RecordedDamageEncounterFileName = "recorded-damage-encounters.json";
    private const int RecordedDamageEncounterSchemaVersion = 1;
    private const int MaxRecordedDamageEncounters = 40;

    private sealed record RecordedDamageEncounterFile(
        int SchemaVersion,
        List<RecordedDamageEncounter> Encounters);

    private static readonly JsonSerializerOptions RecordedDamageEncounterJsonOptions = new()
    {
        WriteIndented = false,
    };

    private static string RecordedDamageEncounterPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedDamageEncounterFileName);

    private static string RecordedDamageEncounterTempPath => RecordedDamageEncounterPath + ".tmp";

    internal void ClearRecordedDamageEncounters()
    {
        lock (recordedDamageEncounterLock)
        {
            recordedDamageEncounters = [];
            nextRecordedDamageEncounterNumber = 1;
        }

        try
        {
            File.Delete(RecordedDamageEncounterPath);
            File.Delete(RecordedDamageEncounterTempPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not clear Better Deaths damage encounter history.");
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

    private void RecordCompletedDamageEncounter(DamageEncounterSnapshot encounter)
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
                currentPullTerritoryId == 0 ? currentTerritoryId : currentPullTerritoryId,
                currentPullTerritoryId == 0 ? currentTerritoryName : currentPullTerritoryName,
                storedSnapshot));
            if (updated.Count > MaxRecordedDamageEncounters)
            {
                updated.RemoveRange(0, updated.Count - MaxRecordedDamageEncounters);
            }

            recordedDamageEncounters = updated;
            snapshot = updated;
        }

        SaveRecordedDamageEncounters(snapshot);
    }

    private static void SaveRecordedDamageEncounters(IReadOnlyList<RecordedDamageEncounter> encounters)
    {
        try
        {
            Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
            var file = new RecordedDamageEncounterFile(
                RecordedDamageEncounterSchemaVersion,
                encounters.ToList());
            File.WriteAllText(
                RecordedDamageEncounterTempPath,
                JsonSerializer.Serialize(file, RecordedDamageEncounterJsonOptions));
            File.Move(RecordedDamageEncounterTempPath, RecordedDamageEncounterPath, true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save Better Deaths damage encounter history.");
        }
    }
}
