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
    private static readonly Regex SharedDamageDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+damage\.(?:\s+(?:HP before hit|HP):\s+.+?\.)?(?:\s+Overkill:\s+(?:[\d,]+|-)\.)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedKnownDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+from\s+(?<action>.+?)\s+by\s+(?<source>.+?)\.\s+(?:HP before hit|HP):\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedMultiHitDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+damage\s+by\s+(?<hits>\d+)\s+hits\s+from\s+(?<source>.+?)\.\s+(?:HP before hit|HP):\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedStatusDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<action>.+?)\s+from\s+(?<source>.+?)\.\s+(?:HP before KO|HP):\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedUnknownDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?:Walled|(?:likely walled/)?non-hit KO)\.(?:\s+(?:HP before KO|HP):\s+.+\.)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly IReadOnlyList<ChatChannelOption> ChatChannelOptions =
    [
        new(DeathChatChannel.SystemMessage, "System Message", string.Empty),
        new(DeathChatChannel.Say, "Say", "/s"),
        new(DeathChatChannel.Party, "Party", "/p"),
        new(DeathChatChannel.Alliance, "Alliance", "/alliance"),
        new(DeathChatChannel.FreeCompany, "Free Company", "/fc"),
        new(DeathChatChannel.CrossWorldLinkshell1, "Cross-world Linkshell 1", "/cwl1"),
        new(DeathChatChannel.CrossWorldLinkshell2, "Cross-world Linkshell 2", "/cwl2"),
        new(DeathChatChannel.CrossWorldLinkshell3, "Cross-world Linkshell 3", "/cwl3"),
        new(DeathChatChannel.CrossWorldLinkshell4, "Cross-world Linkshell 4", "/cwl4"),
        new(DeathChatChannel.CrossWorldLinkshell5, "Cross-world Linkshell 5", "/cwl5"),
        new(DeathChatChannel.CrossWorldLinkshell6, "Cross-world Linkshell 6", "/cwl6"),
        new(DeathChatChannel.CrossWorldLinkshell7, "Cross-world Linkshell 7", "/cwl7"),
        new(DeathChatChannel.CrossWorldLinkshell8, "Cross-world Linkshell 8", "/cwl8"),
    ];

    public static readonly IReadOnlyList<DeathRecapLinkChannelOption> DeathRecapLinkChannelOptions =
    [
        new(DeathRecapLinkChannel.SystemMessage, "System Message"),
        new(DeathRecapLinkChannel.Echo, "Echo"),
        new(DeathRecapLinkChannel.Notice, "Notice"),
        new(DeathRecapLinkChannel.Urgent, "Urgent"),
        new(DeathRecapLinkChannel.ErrorMessage, "Error Message"),
        new(DeathRecapLinkChannel.SystemError, "System Error"),
    ];

    private readonly record struct PlayerLabelCandidate(
        string MemberKey,
        string MemberName,
        int PartyIndex,
        uint ClassJobId,
        string ClassJobName,
        DateTime SeenAtUtc);

    public void PrintDeathInformationToChat(PartyDeathRecord death)
    {
        var timer = FormatCombatTimer(death.PullElapsedSeconds);
        var playerLabel = $"{FormatPlayerDisplayName(death)} ({death.ClassJobName})";
        var prefix = GetChatBrandingPrefix();
        var selection = DeathDisplaySelector.Select(death);
        var causeEvents = selection.Events;
        if (causeEvents.Count == 0)
        {
            RememberOwnSharedDeathPost(death);
            var hpSuffix = selection.Snapshot is null
                ? string.Empty
                : $" HP: {FormatDeathChatHp(selection.Snapshot.CurrentHp, selection.Snapshot.ShieldHp, selection.Snapshot.MaxHp)}.";
            var koLabel = death.EnvironmentalAssessment is { EnvironmentSourceDeath: true }
                ? "Walled"
                : "non-hit KO";
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {koLabel}.{hpSuffix}");
            QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
            QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
            return;
        }

        RememberOwnSharedDeathPost(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        if (damageEvents.Count > 0)
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatDamageLine(damageEvents, selection.Snapshot)}");
        }
        else
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatCauseLine(causeEvents[0], selection.Snapshot)}");
        }

        QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
        QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
        QueueDeathRecapLinkMessage(death);
    }

    public void QueueBetterDeathsChatMessage(string message)
    {
        QueueChat(Configuration.DeathChatChannel, $"{GetChatBrandingPrefix()}{message}");
    }

    public void QueuePlainChatMessage(string message)
    {
        QueueChat(Configuration.DeathChatChannel, message);
    }

    public static string GetChatChannelLabel(DeathChatChannel channel)
    {
        return GetChatChannelOption(channel).Label;
    }

    public static DeathChatChannel GetEffectiveChatChannel(DeathChatChannel channel)
    {
        return GetChatChannelOption(channel).Channel;
    }

    public static string GetDeathRecapLinkChannelLabel(DeathRecapLinkChannel channel)
    {
        return GetDeathRecapLinkChannelOption(channel).Label;
    }

    public static DeathRecapLinkChannel GetEffectiveDeathRecapLinkChannel(DeathRecapLinkChannel channel)
    {
        return GetDeathRecapLinkChannelOption(channel).Channel;
    }

    private static string FormatDeathStatusList(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var groups = new List<string>();
        var defenses = GetPrimaryDisplayPlayerStatuses(death, selection)
            .Where(IsDefensiveStatus)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
        if (defenses.Count > 0)
        {
            groups.Add($"Player: {FormatStatusList(defenses)}");
        }

        groups.AddRange(FormatBossMitigationGroups(selection));

        return groups.Count == 0 ? "none captured" : string.Join(" | ", groups);
    }

    private static string FormatPlayerDebuffStatusList(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var debuffs = GetPrimaryDisplayPlayerStatuses(death, selection)
            .Where(IsPlayerDebuffStatus)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();

        return debuffs.Count == 0 ? "none captured" : FormatStatusList(debuffs);
    }

    private static IReadOnlyList<StatusSnapshot> GetPrimaryDisplayStatusSnapshot(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        return selection.Snapshot?.Statuses ??
            GetPrimaryDisplayCauseEvent(selection.Events)?.Statuses ??
            death.StatusesAtDeath;
    }

    private static IReadOnlyList<StatusSnapshot> GetPrimaryDisplayPlayerStatuses(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        return GetRelevantDeathStatuses(
            GetPrimaryDisplayStatusSnapshot(death, selection)
                .Concat(selection.Events.SelectMany(combatEvent => combatEvent.Statuses)));
    }

    private static IReadOnlyList<string> FormatBossMitigationGroups(DeathDisplaySelection selection)
    {
        return selection.Events
            .SelectMany(combatEvent => combatEvent.SourceStatuses
                .Where(IsBossMitigationStatus)
                .Select(status => new { combatEvent.SourceName, Status = status }))
            .GroupBy(entry => entry.SourceName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var statuses = group
                    .Select(entry => entry.Status)
                    .GroupBy(status => status.Id)
                    .Select(statusGroup => statusGroup.OrderBy(status => status.RemainingTime).First())
                    .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(status => status.Id)
                    .ToList();

                return statuses.Count == 0 ? null : $"{group.Key}: {FormatStatusList(statuses)}";
            })
            .Where(groupText => groupText is not null)
            .Select(groupText => groupText!)
            .ToList();
    }

    public static uint GetMemberKeyHash(string memberKey)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in memberKey)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    public string FormatPlayerDisplayName(PartyDeathRecord death)
    {
        return FormatPlayerDisplayName(death, FindDeathLabelContext(death));
    }

    public string FormatPlayerDisplayName(PartyDeathRecord death, IReadOnlyList<PartyDeathRecord>? context)
    {
        if (!Configuration.RedactPlayerNames)
        {
            return death.MemberName;
        }

        return FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), ToPlayerLabelCandidates(context));
    }

    public string FormatPlayerDisplayName(
        string memberName,
        string memberKey,
        int partyIndex,
        uint classJobId,
        string classJobName)
    {
        if (!Configuration.RedactPlayerNames)
        {
            return memberName;
        }

        return FormatRedactedPlayerLabel(new PlayerLabelCandidate(
            memberKey,
            memberName,
            partyIndex,
            classJobId,
            classJobName,
            DateTime.MinValue));
    }

    public string FormatKnownPlayerName(string name)
    {
        if (!Configuration.RedactPlayerNames || string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return BuildKnownPlayerNameMap().TryGetValue(name, out var label)
            ? label
            : name;
    }

    public string RedactKnownPlayerNamesInText(string text)
    {
        if (!Configuration.RedactPlayerNames || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        foreach (var pair in BuildKnownPlayerNameMap()
                     .OrderByDescending(pair => pair.Key.Length))
        {
            text = text.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private Dictionary<string, string> BuildKnownPlayerNameMap()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddPlayerLabelCandidates(labels, currentMembers.Select(ToPlayerLabelCandidate));
        AddDeathLabelCandidates(labels, currentDeaths);

        foreach (var pull in GetLoadedRecordedPullDetails().AsEnumerable().Reverse())
        {
            AddDeathLabelCandidates(labels, pull.Deaths);
        }

        return labels;
    }

    private static void AddDeathLabelCandidates(Dictionary<string, string> labels, IReadOnlyList<PartyDeathRecord> deaths)
    {
        AddPlayerLabelCandidates(labels, ToPlayerLabelCandidates(deaths));
    }

    private static void AddPlayerLabelCandidates(
        Dictionary<string, string> labels,
        IEnumerable<PlayerLabelCandidate> candidates)
    {
        var context = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.MemberName))
            .ToList();

        foreach (var candidate in context)
        {
            labels.TryAdd(candidate.MemberName, FormatRedactedPlayerLabel(candidate, context));
        }
    }

    private IReadOnlyList<PartyDeathRecord>? FindDeathLabelContext(PartyDeathRecord death)
    {
        if (currentDeaths.Any(candidate => DeathRecordsMatch(candidate, death)))
        {
            return currentDeaths;
        }

        return GetLoadedRecordedPullDetails()
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(pull => pull.Deaths.Any(candidate => DeathRecordsMatch(candidate, death)))
            ?.Deaths;
    }

    private List<PlayerLabelCandidate> GetDeathLabelContext(PartyDeathRecord death)
    {
        return ToPlayerLabelCandidates(FindDeathLabelContext(death));
    }

    private static bool DeathRecordsMatch(PartyDeathRecord left, PartyDeathRecord right)
    {
        return left.SeenAtUtc.Ticks == right.SeenAtUtc.Ticks &&
            string.Equals(left.MemberKey, right.MemberKey, StringComparison.Ordinal);
    }

    private static PlayerLabelCandidate ToPlayerLabelCandidate(PartyDeathRecord death)
    {
        return new PlayerLabelCandidate(
            death.MemberKey,
            death.MemberName,
            death.PartyIndex,
            death.ClassJobId,
            death.ClassJobName,
            death.SeenAtUtc);
    }

    private static PlayerLabelCandidate ToPlayerLabelCandidate(PartyMemberSnapshot member)
    {
        return new PlayerLabelCandidate(
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.ClassJobId,
            member.ClassJobName,
            DateTime.MinValue);
    }

    private static List<PlayerLabelCandidate> ToPlayerLabelCandidates(IReadOnlyList<PartyDeathRecord>? deaths)
    {
        return deaths?.Select(ToPlayerLabelCandidate).ToList() ?? [];
    }

    private static string FormatRedactedPlayerLabel(PlayerLabelCandidate candidate, IReadOnlyList<PlayerLabelCandidate>? context = null)
    {
        var role = GetRedactedRoleLabel(candidate.ClassJobName);
        var sameRoleCandidates = (context ?? [])
            .Where(other => string.Equals(GetRedactedRoleLabel(other.ClassJobName), role, StringComparison.Ordinal))
            .GroupBy(GetPlayerLabelCandidateKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(NormalizedPartyIndex)
                .ThenBy(other => other.SeenAtUtc)
                .ThenBy(other => other.MemberName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(NormalizedPartyIndex)
            .ThenBy(other => other.SeenAtUtc)
            .ThenBy(other => other.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = sameRoleCandidates.FindIndex(other => PlayerLabelCandidatesMatch(other, candidate));
        return $"{role} {(index >= 0 ? index + 1 : GetFallbackRedactedRoleIndex(candidate, role))}";
    }

    private static string GetRedactedRoleLabel(string classJobName)
    {
        var job = classJobName.Trim().ToUpperInvariant();
        return job switch
        {
            "GLA" or "PLD" or "MRD" or "WAR" or "DRK" or "GNB" => "Tank",
            "CNJ" or "WHM" or "SCH" or "AST" or "SGE" => "Healer",
            _ => "DPS",
        };
    }

    private static int GetFallbackRedactedRoleIndex(PlayerLabelCandidate candidate, string role)
    {
        if (candidate.PartyIndex < 0)
        {
            return 1;
        }

        return role switch
        {
            "Tank" => candidate.PartyIndex <= 1 ? candidate.PartyIndex + 1 : 1,
            "Healer" => candidate.PartyIndex is >= 2 and <= 3 ? candidate.PartyIndex - 1 : 1,
            "DPS" => candidate.PartyIndex >= 4 ? candidate.PartyIndex - 3 : Math.Max(1, candidate.PartyIndex + 1),
            _ => Math.Max(1, candidate.PartyIndex + 1),
        };
    }

    private static string GetPlayerLabelCandidateKey(PlayerLabelCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.MemberKey)
            ? $"name:{candidate.MemberName}:{candidate.ClassJobName}"
            : $"key:{candidate.MemberKey}";
    }

    private static bool PlayerLabelCandidatesMatch(PlayerLabelCandidate left, PlayerLabelCandidate right)
    {
        if (!string.IsNullOrWhiteSpace(left.MemberKey) && !string.IsNullOrWhiteSpace(right.MemberKey))
        {
            return string.Equals(left.MemberKey, right.MemberKey, StringComparison.Ordinal);
        }

        return string.Equals(left.MemberName, right.MemberName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassJobName, right.ClassJobName, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizedPartyIndex(PlayerLabelCandidate candidate)
    {
        return candidate.PartyIndex < 0 ? int.MaxValue : candidate.PartyIndex;
    }

    private void OnDeathChatLinkClick(uint commandId, SeString message)
    {
        foreach (var rawPayload in message.Payloads.OfType<RawPayload>())
        {
            if (DeathChatLinkPayload.Decode(rawPayload) is not { } payload)
            {
                continue;
            }

            if (!recapWindow.FocusDeath(payload.DeathSeenAtTicks, payload.MemberKeyHash))
            {
                ChatGui.Print("[Better Deaths] That death recap is no longer available.");
            }

            return;
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (!TryParseSharedDeathPost(message.OriginalMessage.ExtractText(), out var post) &&
                !TryParseSharedDeathPost(message.Message.TextValue, out post))
            {
                return;
            }

            if (IsRecentOwnSharedDeathPost(post))
            {
                AddDebugLog("Ignored own shared Better Deaths recap chat post.");
                return;
            }

            if (FindSharedDeathPost(post) is { } death)
            {
                if (!HasDeathRecapDetails(death))
                {
                    AddDebugLog($"Shared Better Deaths recap for {death.MemberName} has no detail panel to link.");
                    return;
                }

                QueueDetectedSharedRecapLink(death);
                AddDebugLog($"Linked shared Better Deaths recap for {death.MemberName}.");
                return;
            }

            AddDebugLog($"Shared Better Deaths recap did not match a captured death for {post.MemberName}.");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process shared Better Deaths chat recap.");
        }
    }

    private static string FormatCause(CombatEventRecord? cause)
    {
        return cause is null
            ? "Unknown"
            : cause.Kind == DeathEventKind.Status
                ? FormatActionNameForDisplay(cause)
                : $"{FormatActionNameForDisplay(cause)} ({cause.Amount:N0})";
    }

    private static string FormatCause(IReadOnlyList<CombatEventRecord> causes)
    {
        if (causes.Count == 0)
        {
            return "Unknown";
        }

        if (causes.Count == 1)
        {
            return FormatCause(causes[0]);
        }

        var totalDamage = causes
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        return totalDamage > 0
            ? $"{causes.Count} causes ({totalDamage:N0} total)"
            : $"{causes.Count} causes";
    }

    private static string FormatDeathChatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var hpText = maxHp == 0
            ? $"{currentHp:N0}"
            : $"{currentHp:N0} ({(double)currentHp / maxHp:P0})";
        return shieldHp > 0
            ? $"{hpText} + {shieldHp:N0} shield"
            : hpText;
    }

    private string FormatDeathChatCauseLine(CombatEventRecord cause, HpHistorySnapshot? snapshot)
    {
        var hpText = FormatDeathChatHp(snapshot, cause);
        var hpSuffix = hpText is null
            ? string.Empty
            : $" HP: {hpText}.";
        var sourceName = FormatKnownPlayerName(cause.SourceName);
        return cause.Kind == DeathEventKind.Status
            ? $"{FormatActionNameForDisplay(cause)} from {sourceName}.{hpSuffix}"
            : $"{cause.Amount:N0} from {FormatActionNameForDisplay(cause)} by {sourceName}.{hpSuffix}";
    }

    private static string FormatDeathChatDamageLine(IReadOnlyList<CombatEventRecord> damageEvents, HpHistorySnapshot? snapshot)
    {
        var hpDisplay = GetDeathChatHpDisplay(snapshot, damageEvents[0]);
        var hpSuffix = hpDisplay.Text is null
            ? string.Empty
            : $" HP: {hpDisplay.Text}.";
        var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        return $"{totalDamage:N0} damage.{hpSuffix} Overkill: {FormatDeathChatOverkill(totalDamage, hpDisplay.HpBeforeHit)}.";
    }

    private static string? FormatDeathChatHp(HpHistorySnapshot? snapshot, CombatEventRecord fallbackEvent)
    {
        return GetDeathChatHpDisplay(snapshot, fallbackEvent).Text;
    }

    private static DeathChatHpDisplay GetDeathChatHpDisplay(HpHistorySnapshot? snapshot, CombatEventRecord fallbackEvent)
    {
        if (snapshot is not null)
        {
            return new DeathChatHpDisplay(
                FormatDeathChatHp(snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp),
                snapshot.CurrentHp);
        }

        if (fallbackEvent.HpSource != CombatEventHpSource.NoPreHitSample &&
            fallbackEvent.MaxHp > 0 &&
            (fallbackEvent.CurrentHp > 0 || fallbackEvent.ShieldHp > 0))
        {
            return new DeathChatHpDisplay(
                FormatDeathChatHp(fallbackEvent.CurrentHp, fallbackEvent.ShieldHp, fallbackEvent.MaxHp),
                fallbackEvent.CurrentHp);
        }

        return new DeathChatHpDisplay(null, null);
    }

    private static string FormatDeathChatOverkill(ulong incomingDamage, ulong? hpBeforeHit)
    {
        if (hpBeforeHit is null)
        {
            return "-";
        }

        return incomingDamage > hpBeforeHit.Value
            ? $"{incomingDamage - hpBeforeHit.Value:N0}"
            : "0";
    }

    private static string FormatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var effectiveHp = (ulong)currentHp + shieldHp;
        return maxHp == 0
            ? $"{currentHp:N0} + {shieldHp:N0} shield"
            : $"{currentHp:N0} + {shieldHp:N0} shield / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})";
    }

    private static string FormatStatusList(IReadOnlyList<StatusSnapshot> statuses)
    {
        return statuses.Count == 0
            ? "none captured"
            : string.Join(", ", statuses.Select(FormatStatus));
    }

    private static string FormatStatus(StatusSnapshot status)
    {
        var stacks = status.StackCount == 0 ? string.Empty : $" x{status.StackCount}";
        return $"{status.Name}{stacks}";
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void RememberOwnSharedDeathPost(PartyDeathRecord death)
    {
        var post = BuildSharedDeathPost(death);
        var now = DateTime.UtcNow;
        PruneRecentOwnSharedDeathPosts(now);
        recentOwnSharedDeathPosts.Add(new RecentOwnSharedDeathPost(post, now.AddSeconds(OwnSharedRecapSuppressionSeconds)));
    }

    private bool IsRecentOwnSharedDeathPost(SharedDeathPost post)
    {
        var now = DateTime.UtcNow;
        PruneRecentOwnSharedDeathPosts(now);
        return recentOwnSharedDeathPosts.Any(entry => PostsMatch(entry.Post, post));
    }

    private void PruneRecentOwnSharedDeathPosts(DateTime now)
    {
        recentOwnSharedDeathPosts.RemoveAll(entry => entry.ExpiresAtUtc <= now);
    }

    private PartyDeathRecord? FindSharedDeathPost(SharedDeathPost post)
    {
        var candidates = currentDeaths
            .Concat(GetRecordedPullDetailsForSearch().SelectMany(pull => pull.Deaths))
            .Where(death => IsSharedDeathCandidate(death, post))
            .OrderBy(death => MathF.Abs(GetSharedPostElapsedSeconds(death) - post.ElapsedSeconds))
            .ThenByDescending(death => death.SeenAtUtc)
            .ToList();

        var exactMatch = candidates.FirstOrDefault(death => DeathCauseMatchesSharedPost(death, post));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private bool IsSharedDeathCandidate(PartyDeathRecord death, SharedDeathPost post)
    {
        if (!SharedPostMemberMatches(death, post.MemberName))
        {
            return false;
        }

        if (!string.Equals(death.ClassJobName, post.ClassJobName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (MathF.Abs(GetSharedPostElapsedSeconds(death) - post.ElapsedSeconds) > SharedRecapMatchWindowSeconds)
        {
            return false;
        }

        return true;
    }

    private bool SharedPostMemberMatches(PartyDeathRecord death, string postMemberName)
    {
        if (string.Equals(death.MemberName, postMemberName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var redactedName = FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), GetDeathLabelContext(death));
        return string.Equals(redactedName, postMemberName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeathCauseMatchesSharedPost(PartyDeathRecord death, SharedDeathPost post)
    {
        var causeEvents = GetDisplayCauseEvents(death);
        if (post.Amount is null && post.ActionName is null && post.SourceName is null)
        {
            return causeEvents.Count == 0;
        }

        if (post.Amount is null)
        {
            return causeEvents.Any(cause =>
                cause.Kind == DeathEventKind.Status &&
                ActionNamesMatch(cause.ActionName, post.ActionName) &&
                string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
        }

        if (post.HitCount is { } hitCount)
        {
            var damageEvents = causeEvents
                .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
                .ToList();
            var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            if (damageEvents.Count != hitCount || totalDamage != post.Amount.Value)
            {
                return false;
            }

            return post.SourceName is null ||
                damageEvents.All(cause => string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
        }

        if (post.ActionName is null && post.SourceName is null)
        {
            var totalDamage = causeEvents
                .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
                .Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            return totalDamage == post.Amount.Value;
        }

        return causeEvents.Any(cause =>
            post.Amount.Value == cause.Amount &&
            ActionNamesMatch(cause.ActionName, post.ActionName) &&
            string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
    }

    private SharedDeathPost BuildSharedDeathPost(PartyDeathRecord death)
    {
        var causeEvents = GetDisplayCauseEvents(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        var memberName = FormatSharedPostMemberName(death);
        if (damageEvents.Count > 0)
        {
            var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            return new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                null,
                null,
                totalDamage,
                null);
        }

        return causeEvents.FirstOrDefault() is { } cause
            ? new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                FormatActionNameForDisplay(cause),
                cause.SourceName,
                cause.Kind == DeathEventKind.Status ? null : cause.Amount,
                null)
            : new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                null,
                null,
                null,
                null);
    }

    private string FormatSharedPostMemberName(PartyDeathRecord death)
    {
        return Configuration.RedactPlayerNames
            ? FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), GetDeathLabelContext(death))
            : death.MemberName;
    }

    private static float GetSharedPostElapsedSeconds(PartyDeathRecord death)
    {
        return (int)MathF.Max(0.0f, death.PullElapsedSeconds);
    }

    private static bool TryParseSharedDeathPost(string text, out SharedDeathPost post)
    {
        var cleaned = SanitizeChatText(text);
        var damageMatch = SharedDamageDeathPostRegex.Match(cleaned);
        if (damageMatch.Success &&
            TryParseCombatTimer(damageMatch.Groups["timer"].Value, out var damageElapsedSeconds) &&
            TryParseAmount(damageMatch.Groups["amount"].Value, out var damageAmount))
        {
            post = new SharedDeathPost(
                damageElapsedSeconds,
                damageMatch.Groups["name"].Value,
                damageMatch.Groups["job"].Value,
                null,
                null,
                damageAmount,
                null);
            return true;
        }

        var multiHitMatch = SharedMultiHitDeathPostRegex.Match(cleaned);
        if (multiHitMatch.Success &&
            TryParseCombatTimer(multiHitMatch.Groups["timer"].Value, out var multiHitElapsedSeconds) &&
            TryParseAmount(multiHitMatch.Groups["amount"].Value, out var multiHitAmount) &&
            int.TryParse(multiHitMatch.Groups["hits"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hitCount))
        {
            var sourceName = multiHitMatch.Groups["source"].Value;
            post = new SharedDeathPost(
                multiHitElapsedSeconds,
                multiHitMatch.Groups["name"].Value,
                multiHitMatch.Groups["job"].Value,
                null,
                string.Equals(sourceName, "multiple targets", StringComparison.OrdinalIgnoreCase) ? null : sourceName,
                multiHitAmount,
                hitCount);
            return true;
        }

        var knownMatch = SharedKnownDeathPostRegex.Match(cleaned);
        if (knownMatch.Success &&
            TryParseCombatTimer(knownMatch.Groups["timer"].Value, out var knownElapsedSeconds) &&
            TryParseAmount(knownMatch.Groups["amount"].Value, out var amount))
        {
            post = new SharedDeathPost(
                knownElapsedSeconds,
                knownMatch.Groups["name"].Value,
                knownMatch.Groups["job"].Value,
                knownMatch.Groups["action"].Value,
                knownMatch.Groups["source"].Value,
                amount,
                null);
            return true;
        }

        var statusMatch = SharedStatusDeathPostRegex.Match(cleaned);
        if (statusMatch.Success &&
            TryParseCombatTimer(statusMatch.Groups["timer"].Value, out var statusElapsedSeconds))
        {
            post = new SharedDeathPost(
                statusElapsedSeconds,
                statusMatch.Groups["name"].Value,
                statusMatch.Groups["job"].Value,
                statusMatch.Groups["action"].Value,
                statusMatch.Groups["source"].Value,
                null,
                null);
            return true;
        }

        var unknownMatch = SharedUnknownDeathPostRegex.Match(cleaned);
        if (unknownMatch.Success &&
            TryParseCombatTimer(unknownMatch.Groups["timer"].Value, out var unknownElapsedSeconds))
        {
            post = new SharedDeathPost(
                unknownElapsedSeconds,
                unknownMatch.Groups["name"].Value,
                unknownMatch.Groups["job"].Value,
                null,
                null,
                null,
                null);
            return true;
        }

        post = default;
        return false;
    }

    private static bool TryParseCombatTimer(string timer, out int elapsedSeconds)
    {
        var parts = timer.Split(':', 2);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            elapsedSeconds = minutes * 60 + seconds;
            return true;
        }

        elapsedSeconds = 0;
        return false;
    }

    private static bool TryParseAmount(string amountText, out ulong amount)
    {
        return ulong.TryParse(
            amountText.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static bool PostsMatch(SharedDeathPost left, SharedDeathPost right)
    {
        return left.ElapsedSeconds == right.ElapsedSeconds &&
            string.Equals(left.MemberName, right.MemberName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassJobName, right.ClassJobName, StringComparison.OrdinalIgnoreCase) &&
            ActionNamesMatch(left.ActionName, right.ActionName) &&
            string.Equals(left.SourceName, right.SourceName, StringComparison.OrdinalIgnoreCase) &&
            left.Amount == right.Amount &&
            left.HitCount == right.HitCount;
    }

    private static string FormatActionNameForDisplay(CombatEventRecord combatEvent)
    {
        return IsLikelyAutoAttack(combatEvent)
            ? AutoActionDisplayName
            : FormatActionNameForDisplay(combatEvent.ActionName);
    }

    private static string FormatActionNameForDisplay(string actionName)
    {
        return IsAutoAttackActionName(actionName)
            ? AutoActionDisplayName
            : actionName;
    }

    private static bool ActionNamesMatch(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            FormatActionNameForDisplay(left),
            FormatActionNameForDisplay(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyAutoAttack(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage &&
            IsAutoAttackActionName(combatEvent.ActionName);
    }

    private static bool IsAutoAttackActionName(string actionName)
    {
        if (string.Equals(actionName, AutoActionDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string actionPrefix = "Action ";
        return actionName.StartsWith(actionPrefix, StringComparison.Ordinal) &&
            actionName[actionPrefix.Length..].All(char.IsDigit);
    }

    private string GetChatBrandingPrefix()
    {
        return Configuration.RemoveChatBranding ? string.Empty : "[Better Deaths] ";
    }

    private void QueueDeathRecapLink(PartyDeathRecord death, DateTime now)
    {
        if (!Configuration.PostDeathRecapLinksOnDeath)
        {
            return;
        }

        if (!HasDeathRecapDetails(death))
        {
            return;
        }

        pendingDeathRecapLinks.Add(death);
        pendingDeathRecapLinksDueAtUtc = now.AddSeconds(DeathRecapLinkBatchDelaySeconds);
    }

    private static bool HasDeathRecapDetails(PartyDeathRecord death)
    {
        return GetDisplayCauseEvents(death).Count > 0 ||
            death.FatalSequence is { Events.Count: > 0 } ||
            death.FatalSequence is { LogEvents.Count: > 0 } ||
            death.EnvironmentalAssessment is { Confidence: > 0.0f };
    }

    private void FlushPendingDeathRecapLinks(DateTime now)
    {
        if (pendingDeathRecapLinks.Count == 0)
        {
            pendingDeathRecapLinksDueAtUtc = null;
            return;
        }

        if (pendingDeathRecapLinksDueAtUtc is { } dueAt && dueAt > now)
        {
            return;
        }

        var deaths = pendingDeathRecapLinks
            .OrderBy(death => death.SeenAtUtc)
            .ThenBy(death => death.PartyIndex)
            .ToList();
        pendingDeathRecapLinks.Clear();
        pendingDeathRecapLinksDueAtUtc = null;

        PrintDeathRecapLink(deaths[0], GetDeathRecapBatchLabel(deaths));
    }

    private string GetDeathRecapBatchLabel(IReadOnlyList<PartyDeathRecord> deaths)
    {
        var namesText = FormatDeathRecapNames(deaths);
        return deaths.Count switch
        {
            <= 1 => namesText,
            >= 8 => "Party wipe detected",
            _ => $"{deaths.Count} deaths detected ({namesText})",
        };
    }

    private string FormatDeathRecapNames(IReadOnlyList<PartyDeathRecord> deaths)
    {
        const int maxShownNames = 4;
        var names = deaths
            .Select(death => FormatPlayerDisplayName(death, deaths))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (names.Count == 0)
        {
            return "unknown";
        }

        var shownNames = names.Take(maxShownNames).ToList();
        var hiddenCount = names.Count - shownNames.Count;
        return hiddenCount > 0
            ? $"{string.Join(", ", shownNames)}, +{hiddenCount} more"
            : string.Join(", ", shownNames);
    }

    private void PrintDeathRecapLink(PartyDeathRecord death)
    {
        PrintDeathRecapLink(death, FormatPlayerDisplayName(death));
    }

    private void QueueDetectedSharedRecapLink(PartyDeathRecord death)
    {
        QueueDeathRecapLinkMessage(
            death,
            FormatPlayerDisplayName(death),
            PullRecapLinkLabel,
            DateTime.UtcNow.AddMilliseconds(DetectedSharedRecapLinkDelayMs));
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel)
    {
        PrintDeathRecapLink(death, batchLabel, DeathRecapLinkLabel);
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel, string label)
    {
        ChatGui.Print(BuildDeathRecapLinkEntry(death, batchLabel, label));
    }

    private void QueueDeathRecapLinkMessage(PartyDeathRecord death)
    {
        QueueDeathRecapLinkMessage(
            death,
            FormatPlayerDisplayName(death),
            PullRecapLinkLabel,
            DateTime.MinValue);
    }

    private void QueueDeathRecapLinkMessage(PartyDeathRecord death, string batchLabel, string label, DateTime notBeforeUtc)
    {
        queuedChatMessages.Enqueue(QueuedChatMessage.Local(BuildDeathRecapLinkEntry(death, batchLabel, label), notBeforeUtc));
    }

    private XivChatEntry BuildDeathRecapLinkEntry(PartyDeathRecord death, string batchLabel, string label)
    {
        var batchText = string.IsNullOrWhiteSpace(batchLabel) ? string.Empty : $"{batchLabel} ";
        var message = new SeString(
            new TextPayload(GetChatBrandingPrefix()),
            new TextPayload(batchText),
            deathChatLinkPayload,
            new UIForegroundPayload(710),
            new TextPayload(label),
            new UIForegroundPayload(0),
            new DeathChatLinkPayload(death.SeenAtUtc.Ticks, GetMemberKeyHash(death.MemberKey)),
            RawPayload.LinkTerminator);

        return new XivChatEntry
        {
            Type = GetDeathRecapLinkChatType(Configuration.DeathRecapLinkChannel),
            Message = message,
        };
    }

    private void QueueChat(DeathChatChannel channel, string message)
    {
        var effectiveChannel = GetChatChannelOption(channel).Channel;
        foreach (var line in SplitChatMessage(SanitizeChatText(message)))
        {
            queuedChatMessages.Enqueue(
                effectiveChannel == DeathChatChannel.SystemMessage
                    ? QueuedChatMessage.Local(BuildSystemMessageEntry(line), DateTime.MinValue)
                    : QueuedChatMessage.Outgoing(effectiveChannel, line));
        }
    }

    private void FlushQueuedChatMessages(DateTime now)
    {
        if (queuedChatMessages.Count == 0 || nextQueuedChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedChatMessages.Peek();
        if (nextMessage.NotBeforeUtc > now)
        {
            return;
        }

        queuedChatMessages.Dequeue();
        if (nextMessage.LocalEntry is { } localEntry)
        {
            ChatGui.Print(localEntry);
        }
        else
        {
            SendChat(nextMessage.Channel, nextMessage.Message);
        }

        nextQueuedChatMessageAtUtc = now.AddMilliseconds(QueuedChatDelayMs);
    }

    private static unsafe void SendChat(DeathChatChannel channel, string message)
    {
        if (channel == DeathChatChannel.SystemMessage)
        {
            ChatGui.Print(BuildSystemMessageEntry(message));
            return;
        }

        try
        {
            var uiModule = UIModule.Instance();
            var shellModule = RaptureShellModule.Instance();
            if (uiModule == null || shellModule == null)
            {
                Log.Warning("Could not send Better Deaths chat message because the UI shell is unavailable.");
                ChatGui.Print("[Better Deaths] Could not send chat message.");
                return;
            }

            using var command = new Utf8String($"{GetChatChannelOption(channel).Command} {SanitizeChatText(message)}");
            shellModule->ExecuteCommandInner(&command, uiModule);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not send Better Deaths chat message.");
            ChatGui.Print("[Better Deaths] Could not send chat message.");
        }
    }

    private static ChatChannelOption GetChatChannelOption(DeathChatChannel channel)
    {
        var normalizedChannel = channel == DeathChatChannel.Echo
            ? DeathChatChannel.SystemMessage
            : channel;
        return ChatChannelOptions.FirstOrDefault(option => option.Channel == normalizedChannel) ??
            ChatChannelOptions.First(option => option.Channel == DeathChatChannel.Party);
    }

    private static DeathRecapLinkChannelOption GetDeathRecapLinkChannelOption(DeathRecapLinkChannel channel)
    {
        return DeathRecapLinkChannelOptions.FirstOrDefault(option => option.Channel == channel) ??
            DeathRecapLinkChannelOptions.First(option => option.Channel == DeathRecapLinkChannel.SystemMessage);
    }

    private static XivChatType GetDeathRecapLinkChatType(DeathRecapLinkChannel channel)
    {
        return GetDeathRecapLinkChannelOption(channel).Channel switch
        {
            DeathRecapLinkChannel.Echo => XivChatType.Echo,
            DeathRecapLinkChannel.Notice => XivChatType.Notice,
            DeathRecapLinkChannel.Urgent => XivChatType.Urgent,
            DeathRecapLinkChannel.ErrorMessage => XivChatType.ErrorMessage,
            DeathRecapLinkChannel.SystemError => XivChatType.SystemError,
            _ => XivChatType.SystemMessage,
        };
    }

    private static XivChatEntry BuildSystemMessageEntry(string message)
    {
        return new XivChatEntry
        {
            Type = XivChatType.SystemMessage,
            Message = new SeString(new TextPayload(message)),
        };
    }

    private static IEnumerable<string> SplitChatMessage(string message)
    {
        if (message.Length <= MaxQueuedChatMessageLength)
        {
            yield return message;
            yield break;
        }

        var remaining = message;
        while (remaining.Length > MaxQueuedChatMessageLength)
        {
            var splitAt = remaining.LastIndexOf(' ', MaxQueuedChatMessageLength);
            if (splitAt <= 0)
            {
                splitAt = MaxQueuedChatMessageLength;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static string SanitizeChatText(string message)
    {
        var cleaned = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned;
    }

    private readonly record struct SharedDeathPost(
        float ElapsedSeconds,
        string MemberName,
        string ClassJobName,
        string? ActionName,
        string? SourceName,
        ulong? Amount,
        int? HitCount);

    private readonly record struct DeathChatHpDisplay(
        string? Text,
        ulong? HpBeforeHit);

    private readonly record struct RecentOwnSharedDeathPost(
        SharedDeathPost Post,
        DateTime ExpiresAtUtc);

    private readonly record struct QueuedChatMessage(
        DeathChatChannel Channel,
        string Message,
        XivChatEntry? LocalEntry,
        DateTime NotBeforeUtc)
    {
        public static QueuedChatMessage Outgoing(DeathChatChannel channel, string message)
        {
            return new QueuedChatMessage(channel, message, null, DateTime.MinValue);
        }

        public static QueuedChatMessage Local(XivChatEntry entry, DateTime notBeforeUtc)
        {
            return new QueuedChatMessage(DeathChatChannel.Echo, string.Empty, entry, notBeforeUtc);
        }
    }
}
