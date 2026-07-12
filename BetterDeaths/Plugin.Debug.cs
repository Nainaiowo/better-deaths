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
    private sealed record DebugCaptureFileRecord(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        uint TerritoryId,
        string TerritoryName,
        string Kind,
        JsonElement Data);

    private sealed record DebugActionEffectRecord(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        string MemberKey,
        string MemberName,
        int PartyIndex,
        uint SourceEntityId,
        string SourceName,
        uint ActionId,
        string ActionName,
        uint ActionIconId,
        string Kind,
        int KindId,
        uint Amount,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        string DamageType,
        int DamageTypeId,
        bool Critical,
        bool DirectHit,
        bool Blocked,
        bool Parried,
        string Detail,
        string? EventIdentity,
        uint EventOrdinal,
        uint ActionSequence,
        string HpSource,
        int HpSourceId,
        DateTime? ResultSeenAtUtc,
        uint ResultCurrentHp,
        uint ResultShieldHp,
        uint ResultMaxHp,
        IReadOnlyList<StatusSnapshot> Statuses,
        IReadOnlyList<StatusSnapshot> SourceStatuses,
        IReadOnlyList<StatusSnapshot> ResultStatuses);

    public IReadOnlyList<DebugLogEntry> DebugLogEntries => debugLogEntries;

    public IReadOnlyList<DebugStatusSnapshot> DebugStatusSnapshots => debugStatusSnapshotsByMember.Values
        .OrderBy(snapshot => snapshot.PartyIndex)
        .ThenBy(snapshot => snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugEffectResultSnapshot> DebugEffectResultSnapshots => debugEffectResultSnapshotsByTarget.Values
        .OrderBy(snapshot => snapshot.PullElapsedSeconds)
        .ThenBy(snapshot => snapshot.TargetName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugEffectResultSnapshot> DebugEffectResultHistory => debugEffectResultHistory
        .OrderBy(snapshot => snapshot.SeenAtUtc)
        .ThenBy(snapshot => snapshot.TargetName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugActorControlEvent> DebugActorControlEvents => debugActorControlEvents
        .OrderBy(entry => entry.SeenAtUtc)
        .ThenBy(entry => entry.Category)
        .ToList();

    public IReadOnlyList<AddonInspectorEvent> AddonInspectorEvents => addonInspectorEvents
        .OrderByDescending(entry => entry.SeenAtUtc)
        .ThenBy(entry => entry.AddonName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public AddonInspectorSnapshot? AddonInspectorSnapshot => addonInspectorSnapshot;

    public bool DebugIsDutyCaptureActive => IsDutyCaptureActive();

    public bool DebugIsPvPCaptureBlocked => IsPvPCaptureBlocked();

    public bool DebugIsInCombat => Condition[ConditionFlag.InCombat];

    public bool DebugShouldCaptureLiveCombat => ShouldCaptureLiveCombat(DateTime.UtcNow);

    public bool DebugEffectResultHookEnabled => effectResultHookEnabled;

    public bool DebugActorControlHookEnabled => actorControlHookEnabled;

    public bool DebugMapEffectHookEnabled => mapEffectHookEnabled;

    public bool DebugFreezeOnDeathEnabled => debugFreezeOnDeathEnabled;

    public bool DebugCaptureFrozen => debugCaptureFrozen;

    public string DebugCaptureFilePath => DebugCaptureFileFullPath;

    public long DebugCaptureFileSizeBytes => GetDebugCaptureFileSizeBytes();

    public long DebugCaptureMaxFileSizeBytes => MaxDebugCaptureFileBytes;

    public string LocalDataDirectoryPath => PluginInterface.ConfigDirectory.FullName;

    public long LocalDataDirectorySizeBytes => GetDirectorySizeBytes(PluginInterface.ConfigDirectory.FullName);

    public long RecordedPullStorageSizeBytes => GetRecordedPullStorageSizeBytes();

    public int RecordedPullDetailFileCount => GetRecordedPullDetailFileCount();

    public int DebugCaptureQueuedLineCount
    {
        get
        {
            lock (debugCaptureFileLock)
            {
                return debugCaptureFileLines.Count;
            }
        }
    }

    public void SetShowDebugTab(bool show)
    {
        if (Configuration.ShowDebugTab == show)
        {
            return;
        }

        Configuration.ShowDebugTab = show;
        if (show)
        {
            RegisterAddonInspectorLifecycleListeners();
        }
        else
        {
            UnregisterAddonInspectorLifecycleListeners();
        }

        SaveConfiguration();
    }

    public void SetDebugLogEnabled(bool enabled)
    {
        Configuration.DebugLogEnabled = enabled;
        SaveConfiguration();
    }

    public void SetDebugSaveToFileEnabled(bool enabled)
    {
        Configuration.DebugSaveToFileEnabled = enabled;
        SaveConfiguration();
        if (!enabled)
        {
            FlushDebugCaptureFile(force: true);
        }
    }

    public void SetDebugFreezeOnDeathEnabled(bool enabled)
    {
        debugFreezeOnDeathEnabled = enabled;
        if (!enabled && debugCaptureFrozen)
        {
            debugCaptureFrozen = false;
            AddDebugLog("Debug capture resumed.");
        }
    }

    public void SetDebugCaptureFrozen(bool frozen)
    {
        if (debugCaptureFrozen == frozen)
        {
            return;
        }

        debugCaptureFrozen = frozen;
        AddDebugLog(frozen ? "Debug capture frozen." : "Debug capture resumed.");
    }

    public void ClearDebugLog()
    {
        debugLogEntries.Clear();
        debugStatusSnapshotsByMember.Clear();
        debugStatusPersistSignaturesByMember.Clear();
        debugEffectResultSnapshotsByTarget.Clear();
        debugEffectResultHistory.Clear();
        debugActorControlEvents.Clear();
        addonInspectorEvents.Clear();
        addonInspectorEventSeenAtBySignature.Clear();
        addonInspectorSnapshot = null;
        debugCaptureFrozen = false;
    }

    public void ClearAddonInspector()
    {
        addonInspectorEvents.Clear();
        addonInspectorEventSeenAtBySignature.Clear();
        addonInspectorSnapshot = null;
    }

    public void CaptureAddonInspectorSnapshot(string addonName)
    {
        addonName = addonName.Trim();
        if (string.IsNullOrWhiteSpace(addonName))
        {
            addonInspectorSnapshot = CreateAddonInspectorErrorSnapshot(addonName, "Enter an addon name first.");
            return;
        }

        try
        {
            var addon = GameGui.GetAddonByName(addonName);
            addonInspectorSnapshot = CaptureAddonInspectorSnapshot(addonName, addon);
        }
        catch (Exception ex)
        {
            addonInspectorSnapshot = CreateAddonInspectorErrorSnapshot(addonName, ex.Message);
            Log.Debug(ex, "Could not capture Better Deaths addon inspector snapshot for {AddonName}.", addonName);
        }
    }

    public void ClearSavedDebugCaptureFile()
    {
        lock (debugCaptureFileLock)
        {
            debugCaptureFileLines.Clear();
        }

        try
        {
            if (File.Exists(DebugCaptureFileFullPath))
            {
                File.Delete(DebugCaptureFileFullPath);
            }

            if (File.Exists(DebugCaptureTempFilePath))
            {
                File.Delete(DebugCaptureTempFilePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not clear Better Deaths debug capture file.");
        }
    }

    private void RegisterAddonInspectorLifecycleListeners()
    {
        if (addonInspectorLifecycleRegistered)
        {
            return;
        }

        foreach (var eventType in AddonInspectorLifecycleEvents)
        {
            AddonLifecycle.RegisterListener(eventType, OnAddonInspectorLifecycleEvent);
        }

        addonInspectorLifecycleRegistered = true;
    }

    private void UnregisterAddonInspectorLifecycleListeners()
    {
        if (!addonInspectorLifecycleRegistered)
        {
            return;
        }

        AddonLifecycle.UnregisterListener(OnAddonInspectorLifecycleEvent);
        addonInspectorLifecycleRegistered = false;
    }

    private void OnAddonInspectorLifecycleEvent(AddonEvent eventType, AddonArgs args)
    {
        if (disposing)
        {
            return;
        }

        try
        {
            var addon = args.Addon;
            var isKnown = addon.Address != 0 && !addon.IsNull;
            var now = DateTime.UtcNow;
            var signature = $"{eventType}|{args.AddonName}|{addon.Address}";
            if (addonInspectorEventSeenAtBySignature.TryGetValue(signature, out var lastSeen) &&
                now - lastSeen < TimeSpan.FromSeconds(AddonInspectorDuplicateSuppressSeconds))
            {
                return;
            }

            addonInspectorEventSeenAtBySignature[signature] = now;
            addonInspectorEvents.Add(new AddonInspectorEvent(
                now,
                eventType.ToString(),
                args.AddonName,
                addon.Address,
                isKnown && addon.IsReady,
                isKnown && addon.IsVisible));

            while (addonInspectorEvents.Count > MaxAddonInspectorEvents)
            {
                addonInspectorEvents.RemoveAt(0);
            }

            foreach (var expiredKey in addonInspectorEventSeenAtBySignature
                         .Where(pair => now - pair.Value > TimeSpan.FromMinutes(5))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                addonInspectorEventSeenAtBySignature.Remove(expiredKey);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths addon inspector lifecycle event.");
        }
    }

    private static AddonInspectorSnapshot CreateAddonInspectorErrorSnapshot(string addonName, string error)
    {
        return new AddonInspectorSnapshot(
            DateTime.UtcNow,
            addonName,
            0,
            false,
            false,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0,
            [],
            [],
            error);
    }

    private unsafe AddonInspectorSnapshot CaptureAddonInspectorSnapshot(string addonName, AtkUnitBasePtr addon)
    {
        if (addon.Address == 0 || addon.IsNull)
        {
            return CreateAddonInspectorErrorSnapshot(
                addonName,
                "Addon was not found. Open the game window first, then snapshot again.");
        }

        var nodes = CaptureAddonInspectorNodes((AtkUnitBase*)addon.Address, out var nodeCount);
        return new AddonInspectorSnapshot(
            DateTime.UtcNow,
            addonName,
            addon.Address,
            addon.IsReady,
            addon.IsVisible,
            addon.X,
            addon.Y,
            addon.Width,
            addon.Height,
            nodeCount,
            nodes,
            CaptureAddonInspectorAtkValues(addon),
            null);
    }

    private static unsafe IReadOnlyList<AddonInspectorNode> CaptureAddonInspectorNodes(
        AtkUnitBase* unit,
        out int nodeCount)
    {
        var nodes = new List<AddonInspectorNode>();
        nodeCount = 0;
        if (unit is null)
        {
            return nodes;
        }

        CaptureAddonInspectorNode(unit->RootNode, nodes, ref nodeCount, 0);
        return nodes;
    }

    private static unsafe void CaptureAddonInspectorNode(
        AtkResNode* node,
        List<AddonInspectorNode> nodes,
        ref int nodeCount,
        int depth)
    {
        if (node is null || nodes.Count >= MaxAddonInspectorNodes || depth > 80)
        {
            return;
        }

        var current = node;
        while (current is not null && nodes.Count < MaxAddonInspectorNodes)
        {
            nodeCount++;
            nodes.Add(new AddonInspectorNode(
                nodes.Count,
                current->NodeId,
                current->Type.ToString(),
                current->IsVisible(),
                current->X,
                current->Y,
                current->Width,
                current->Height,
                ReadAddonInspectorNodeText(current)));

            if (current->ChildNode is not null)
            {
                CaptureAddonInspectorNode(current->ChildNode, nodes, ref nodeCount, depth + 1);
            }

            current = current->NextSiblingNode;
        }
    }

    private static unsafe string? ReadAddonInspectorNodeText(AtkResNode* node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            var textNode = node->GetAsAtkTextNode();
            if (textNode is null)
            {
                return null;
            }

            var text = textNode->NodeText.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AddonInspectorValue> CaptureAddonInspectorAtkValues(AtkUnitBasePtr addon)
    {
        var values = new List<AddonInspectorValue>();
        try
        {
            var index = 0;
            foreach (var atkValue in addon.AtkValues)
            {
                if (values.Count >= MaxAddonInspectorAtkValues)
                {
                    break;
                }

                values.Add(new AddonInspectorValue(
                    index,
                    atkValue.ValueType.ToString(),
                    FormatAddonInspectorAtkValue(atkValue)));
                index++;
            }
        }
        catch
        {
            values.Add(new AddonInspectorValue(0, "Error", "Could not read AtkValues."));
        }

        return values;
    }

    private static string FormatAddonInspectorAtkValue(AtkValuePtr atkValue)
    {
        try
        {
            var value = atkValue.GetValue();
            return value?.ToString() ?? "-";
        }
        catch (Exception ex)
        {
            return $"Unreadable: {ex.Message}";
        }
    }

    private void TrackDebugStatusSnapshots(IEnumerable<PartyMemberSnapshot> members, DateTime now)
    {
        if (!Configuration.DebugLogEnabled || debugCaptureFrozen)
        {
            return;
        }

        foreach (var member in members)
        {
            var statuses = member.Statuses.ToList();
            if (debugStatusSnapshotsByMember.TryGetValue(member.MemberKey, out var existing))
            {
                statuses = MergeDebugStatuses(existing.Statuses, statuses);
            }

            debugStatusSnapshotsByMember[member.MemberKey] = new DebugStatusSnapshot(
                now,
                CurrentPullElapsedSeconds,
                member.MemberKey,
                member.MemberName,
                member.PartyIndex,
                member.ClassJobId,
                member.ClassJobName,
                member.CurrentHp,
                member.ShieldHp,
                member.MaxHp,
                member.IsDead,
                member.IsPartyMember,
                statuses);
            var snapshot = debugStatusSnapshotsByMember[member.MemberKey];
            var signature = BuildDebugStatusPersistSignature(snapshot);
            if (!debugStatusPersistSignaturesByMember.TryGetValue(member.MemberKey, out var existingSignature) ||
                !string.Equals(signature, existingSignature, StringComparison.Ordinal))
            {
                debugStatusPersistSignaturesByMember[member.MemberKey] = signature;
                QueueDebugCaptureRecord("StatusSnapshot", snapshot);
            }
        }
    }

    private static List<StatusSnapshot> MergeDebugStatuses(
        IReadOnlyList<StatusSnapshot> existingStatuses,
        IReadOnlyList<StatusSnapshot> currentStatuses)
    {
        var merged = new Dictionary<(uint Id, uint SourceId), StatusSnapshot>();
        foreach (var status in existingStatuses)
        {
            merged[(status.Id, status.SourceId)] = status;
        }

        foreach (var status in currentStatuses)
        {
            merged[(status.Id, status.SourceId)] = status;
        }

        return merged.Values.ToList();
    }

    private static string BuildDebugStatusPersistSignature(DebugStatusSnapshot snapshot)
    {
        return string.Join(
            "|",
            snapshot.IsDead ? "dead" : "alive",
            snapshot.CurrentHp,
            snapshot.ShieldHp,
            snapshot.MaxHp,
            string.Join(
                ";",
                snapshot.Statuses
                    .OrderBy(status => status.Id)
                    .ThenBy(status => status.SourceId)
                    .Select(status => $"{status.Id}:{status.SourceId}:{status.StackCount}")));
    }

    private void ClearDebugDataForDutyEnter()
    {
        if (debugStatusSnapshotsByMember.Count == 0 &&
            debugEffectResultSnapshotsByTarget.Count == 0 &&
            debugEffectResultHistory.Count == 0 &&
            debugActorControlEvents.Count == 0 &&
            debugLogEntries.Count == 0 &&
            !debugCaptureFrozen)
        {
            return;
        }

        debugLogEntries.Clear();
        debugStatusSnapshotsByMember.Clear();
        debugStatusPersistSignaturesByMember.Clear();
        debugEffectResultSnapshotsByTarget.Clear();
        debugEffectResultHistory.Clear();
        debugActorControlEvents.Clear();
        debugCaptureFrozen = false;
        AddDebugLog("Cleared debug data for duty enter.");
    }

    private void AddDebugLog(string message)
    {
        if (!Configuration.DebugLogEnabled)
        {
            return;
        }

        var entry = new DebugLogEntry(DateTime.UtcNow, CurrentPullElapsedSeconds, message);
        debugLogEntries.Add(entry);
        while (debugLogEntries.Count > MaxDebugLogEntries)
        {
            debugLogEntries.RemoveAt(0);
        }

        QueueDebugCaptureRecord("DebugLog", entry);
    }

    private void QueueDebugCaptureRecord<T>(string kind, T data)
    {
        if (!Configuration.DebugLogEnabled || !Configuration.DebugSaveToFileEnabled)
        {
            return;
        }

        try
        {
            var record = new DebugCaptureFileRecord(
                DateTime.UtcNow,
                CurrentPullElapsedSeconds,
                currentTerritoryId,
                currentTerritoryName,
                kind,
                JsonSerializer.SerializeToElement(data, DebugCaptureJsonOptions));
            var line = JsonSerializer.Serialize(record, DebugCaptureJsonOptions);
            lock (debugCaptureFileLock)
            {
                debugCaptureFileLines.Enqueue(line);
                while (debugCaptureFileLines.Count > MaxQueuedDebugCaptureFileLines)
                {
                    debugCaptureFileLines.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not queue Better Deaths debug capture row.");
        }
    }

    private void FlushDebugCaptureFile(DateTime? now = null, bool force = false)
    {
        if (!force && (!Configuration.DebugLogEnabled || !Configuration.DebugSaveToFileEnabled))
        {
            return;
        }

        var currentTime = now ?? DateTime.UtcNow;
        List<string> lines = [];
        lock (debugCaptureFileLock)
        {
            if (debugCaptureFileLines.Count == 0)
            {
                return;
            }

            if (!force &&
                debugCaptureFileLines.Count < 500 &&
                currentTime - lastDebugCaptureFlushAtUtc < DebugCaptureFlushInterval)
            {
                return;
            }

            while (debugCaptureFileLines.Count > 0)
            {
                lines.Add(debugCaptureFileLines.Dequeue());
            }
        }

        try
        {
            Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
            File.AppendAllLines(DebugCaptureFileFullPath, lines, Encoding.UTF8);
            TrimDebugCaptureFileToCap();
            lastDebugCaptureFlushAtUtc = currentTime;
        }
        catch (Exception ex)
        {
            lastDebugCaptureFlushAtUtc = currentTime;
            Log.Warning(ex, "Could not write Better Deaths debug capture file.");
        }
    }

    private static long GetDebugCaptureFileSizeBytes()
    {
        try
        {
            return File.Exists(DebugCaptureFileFullPath)
                ? new FileInfo(DebugCaptureFileFullPath).Length
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void TrimDebugCaptureFileToCap()
    {
        try
        {
            if (!File.Exists(DebugCaptureFileFullPath) ||
                new FileInfo(DebugCaptureFileFullPath).Length <= MaxDebugCaptureFileBytes)
            {
                return;
            }

            var lines = File.ReadAllLines(DebugCaptureFileFullPath, Encoding.UTF8);
            var retainedLines = new List<string>();
            var retainedBytes = 0L;
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index];
                var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (retainedLines.Count > 0 && retainedBytes + lineBytes > DebugCaptureTrimTargetBytes)
                {
                    break;
                }

                retainedLines.Add(line);
                retainedBytes += lineBytes;
            }

            retainedLines.Reverse();
            File.WriteAllLines(DebugCaptureTempFilePath, retainedLines, Encoding.UTF8);
            File.Move(DebugCaptureTempFilePath, DebugCaptureFileFullPath, true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not trim Better Deaths debug capture file.");
        }
    }
}
