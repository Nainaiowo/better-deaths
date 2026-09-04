using BetterDeaths.DamageParsing;
using BetterDeaths.WtfDig;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly DamageParityController damageParity = new();
    private bool showDamageParityPlayers = true;
    private bool showDamageParityAbilities = true;

    private void DrawDamageMeterDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Damage meter diagnostics###DamageMeterDiagnostics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.TextDisabled("Opt-in traces for comparing a completed encounter with the finalized report event stream.");
        var traceEnabled = configuration.DebugDamageMeterTraceEnabled;
        if (DrawThemedCheckbox("Enable damage-meter trace", ref traceEnabled))
        {
            plugin.SetDebugDamageMeterTraceEnabled(traceEnabled);
        }

        if (!traceEnabled)
        {
            return;
        }

        if (!configuration.DebugLogEnabled || !configuration.DebugSaveToFileEnabled)
        {
            ImGui.TextColored(
                WarningColor,
                "Enable debug capture and Save debug file above to record the selected trace rows and encounter export.");
        }

        DrawDamageTraceCategory("Action packets", DamageMeterDebugTraceCategory.ActionPackets);
        ImGui.SameLine();
        DrawDamageTraceCategory("Status changes", DamageMeterDebugTraceCategory.StatusChanges);
        ImGui.SameLine();
        DrawDamageTraceCategory("Periodic ticks", DamageMeterDebugTraceCategory.PeriodicTicks);
        DrawDamageTraceCategory("Parsed events", DamageMeterDebugTraceCategory.ParsedEvents);
        ImGui.SameLine();
        DrawDamageTraceCategory("Encounter summary", DamageMeterDebugTraceCategory.EncounterSummary);

        var exportEncounter = configuration.DebugDamageMeterEncounterExportEnabled;
        if (DrawThemedCheckbox("Keep latest full encounter export", ref exportEncounter))
        {
            plugin.SetDebugDamageMeterEncounterExportEnabled(exportEncounter);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Keeps one overwrite-only JSON file with the complete parsed event list and unredacted actor names. Normal encounter history remains compact.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear encounter export"))
        {
            plugin.ClearDamageMeterDiagnosticEncounter();
            damageParity.ClearResult();
        }

        ImGui.TextDisabled(
            $"Encounter export: {FormatByteSize(plugin.DamageMeterDiagnosticEncounterFileSizeBytes)} | {plugin.DamageMeterDiagnosticEncounterFilePath}");

        DrawDamageParityComparison();
    }

    private void DrawDamageTraceCategory(string label, DamageMeterDebugTraceCategory category)
    {
        var enabled = configuration.DebugDamageMeterTraceCategories.HasFlag(category);
        if (DrawThemedCheckbox($"{label}##DamageTrace{category}", ref enabled))
        {
            plugin.SetDebugDamageMeterTraceCategory(category, enabled);
        }
    }

    private void DrawDamageParityComparison()
    {
        ImGui.Separator();
        ImGui.TextColored(LeadUpGoldColor, "Report comparison");
        ImGui.TextDisabled("Local effective damage / report allocated damage. Independent periodic estimates are listed separately.");

        var state = damageParity.Snapshot();
        var input = state.Input;
        ImGui.SetNextItemWidth(MathF.Max(300.0f, ImGui.GetContentRegionAvail().X * 0.55f));
        if (ImGui.InputText("Report link##DamageParityReport", ref input, 256))
        {
            damageParity.SetInput(input);
        }

        var encounter = plugin.GetLatestDamageMeterDiagnosticEncounter();
        if (encounter is null)
        {
            ImGui.TextDisabled("No completed local damage encounter is available yet.");
        }
        else
        {
            ImGui.TextDisabled(
                $"Local encounter: {encounter.CapturedAtUtc.ToLocalTime():g} | {encounter.Snapshot.DurationSeconds:F2}s | " +
                $"{encounter.Snapshot.Sources.Count:N0} sources | {encounter.Snapshot.Events.Count:N0} retained events");
        }

        ImGui.BeginDisabled(state.Loading || encounter is null || string.IsNullOrWhiteSpace(state.Input));
        if (ImGui.Button("Compare latest encounter"))
        {
            damageParity.Compare(encounter!);
        }

        ImGui.EndDisabled();
        if (state.Loading)
        {
            ImGui.SameLine();
            ImGui.TextColored(LeadUpGoldColor, "Loading report events...");
            ImGui.SameLine();
            if (ImGui.Button("Cancel comparison"))
            {
                damageParity.Cancel();
            }
        }
        else if (state.Comparison is not null || state.Error is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear comparison"))
            {
                damageParity.ClearResult();
            }
        }

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            ImGui.TextColored(SpamWarningColor, state.Error);
        }

        if (state.Comparison is not { } comparison)
        {
            return;
        }

        ImGui.TextUnformatted($"{comparison.ReportTitle} | Fight {comparison.FightId}: {comparison.FightName}");
        DrawDamageParitySummary(comparison);
        foreach (var warning in comparison.Warnings)
        {
            ImGui.TextWrapped(warning);
        }

        DrawThemedCheckbox("Player totals##DamageParityPlayers", ref showDamageParityPlayers);
        ImGui.SameLine();
        DrawThemedCheckbox("Ability differences##DamageParityAbilities", ref showDamageParityAbilities);

        if (showDamageParityPlayers)
        {
            DrawDamageParityPlayers(comparison.Players);
        }

        if (showDamageParityAbilities)
        {
            DrawDamageParityAbilities(comparison.Abilities);
        }
    }

    private static void DrawDamageParitySummary(DamageParityComparison comparison)
    {
        if (!ImGui.BeginTable(
                "##DamageParitySummary",
                6,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Metric", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Local", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Report", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Difference", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Difference %", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Events", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableHeadersRow();

        DrawDamageParitySummaryRow(
            "Event damage",
            comparison.LocalDamage,
            comparison.ReferenceDamage,
            comparison.LocalEventCount,
            comparison.ReferenceEventCount);
        DrawDamageParitySummaryRow(
            "Direct damage",
            comparison.LocalDirectDamage,
            comparison.ReferenceDirectDamage);
        DrawDamageParitySummaryRow(
            "Periodic damage",
            comparison.LocalPeriodicDamage,
            comparison.ReferencePeriodicDamage,
            null,
            comparison.ReferenceSimulatedPeriodicEventCount);
        DrawDamageParitySummaryRow(
            "Encounter DPS",
            comparison.LocalEncounterDps,
            comparison.ReferenceEncounterDps);
        DrawDamageParitySummaryRow(
            "Duration (seconds)",
            comparison.LocalDurationSeconds,
            comparison.ReferenceDurationSeconds,
            decimals: 3);
        ImGui.EndTable();

        if (comparison.ReferenceSimulatedPeriodicEventCount > 0)
        {
            ImGui.TextDisabled(
                $"Report periodic: allocated {comparison.ReferenceSimulatedPeriodicDamage:N0} | " +
                $"finalized {comparison.ReferenceFinalizedPeriodicDamage:N0} | missing finalized ticks {comparison.ReferenceMissingFinalizedTicks:N0}");
        }

        ImGui.TextDisabled($"Combined report ticks excluded: {comparison.ReferenceCombinedPeriodicDamageExcluded:N0}");
        if (comparison.LocalEventCount > 0)
        {
            ImGui.TextDisabled($"Local observed damage: {comparison.LocalRawDamage:N0} | independent periodic estimates: " +
                $"{comparison.LocalSimulatedPeriodicDamage:N0} across {comparison.LocalSimulatedPeriodicTicks:N0} ticks");
        }

        if (comparison.ReferenceUnassignedPetDamage > 0.0)
        {
            ImGui.TextColored(
                WarningColor,
                $"Unassigned pet damage: {comparison.ReferenceUnassignedPetDamage:N0}. It remains in the report total but cannot be assigned when multiple possible owners share a job.");
        }
    }

    private static void DrawDamageParitySummaryRow(
        string label,
        double local,
        double reference,
        int? localEvents = null,
        int? referenceEvents = null,
        int decimals = 0)
    {
        var format = decimals == 0 ? "N0" : $"N{decimals}";
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(local.ToString(format));
        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(reference.ToString(format));
        ImGui.TableSetColumnIndex(3);
        ImGui.TextUnformatted(FormatDamageParityDifference(local - reference, decimals));
        ImGui.TableSetColumnIndex(4);
        ImGui.TextUnformatted(FormatDamageParityPercent(DamageParityComparer.DifferencePercent(local, reference)));
        ImGui.TableSetColumnIndex(5);
        ImGui.TextDisabled(localEvents is null && referenceEvents is null
            ? "-"
            : $"{localEvents?.ToString("N0") ?? "-"} / {referenceEvents?.ToString("N0") ?? "-"}");
    }

    private static void DrawDamageParityPlayers(IReadOnlyList<DamageParityPlayerComparison> players)
    {
        ImGui.TextColored(LeadUpGoldColor, "Players");
        if (!ImGui.BeginTable(
                "##DamageParityPlayers",
                7,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("Local", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Report", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Difference", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Difference %", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Local periodic", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Report periodic", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableHeadersRow();
        foreach (var row in players)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(row.PlayerName);
            if (!row.HasLocalData || !row.HasReferenceData)
            {
                ImGui.SameLine();
                ImGui.TextColored(WarningColor, row.HasLocalData ? "(report unmatched)" : "(local unmatched)");
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(row.LocalDamage.ToString("N0"));
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(row.ReferenceDamage.ToString("N0"));
            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(FormatDamageParityDifference(row.Difference));
            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(FormatDamageParityPercent(row.DifferencePercent));
            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(row.LocalPeriodicDamage.ToString("N0"));
            ImGui.TableSetColumnIndex(6);
            ImGui.TextUnformatted(row.ReferencePeriodicDamage.ToString("N0"));
        }

        ImGui.EndTable();
    }

    private static void DrawDamageParityAbilities(IReadOnlyList<DamageParityAbilityComparison> abilities)
    {
        ImGui.TextColored(LeadUpGoldColor, "Largest ability differences");
        ImGui.TextDisabled("The 30 largest absolute differences, matched by action or status ID.");
        if (!ImGui.BeginTable(
                "##DamageParityAbilities",
                6,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Local", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Report", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Difference", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Difference %", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        ImGui.TableHeadersRow();
        foreach (var row in abilities.Take(30))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(row.PlayerName);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(row.AbilityName);
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(row.LocalDamage.ToString("N0"));
            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(row.ReferenceDamage.ToString("N0"));
            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(FormatDamageParityDifference(row.Difference));
            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(FormatDamageParityPercent(row.DifferencePercent));
        }

        ImGui.EndTable();
    }

    private static string FormatDamageParityDifference(double value, int decimals = 0)
    {
        var formatted = value.ToString($"N{decimals}");
        return value > 0.0 ? "+" + formatted : formatted;
    }

    private static string FormatDamageParityPercent(double? value)
    {
        if (value is null)
        {
            return "-";
        }

        var formatted = value.Value.ToString("0.000");
        return (value > 0.0 ? "+" : string.Empty) + formatted + "%";
    }
}

internal sealed record DamageParityViewState(
    string Input,
    bool Loading,
    DamageParityComparison? Comparison,
    string? Error);

internal sealed class DamageParityController : IDisposable
{
    private readonly object sync = new();
    private readonly FflogsClient client = new();
    private CancellationTokenSource? cancellation;
    private long generation;
    private DamageParityViewState state = new(string.Empty, false, null, null);

    internal DamageParityViewState Snapshot()
    {
        lock (sync)
        {
            return state;
        }
    }

    internal void SetInput(string input)
    {
        lock (sync)
        {
            state = state with { Input = input };
        }
    }

    internal void Compare(DamageMeterDiagnosticEncounter local)
    {
        FflogsReportInput? parsed;
        long operationGeneration;
        CancellationToken token;
        lock (sync)
        {
            parsed = FflogsClient.ParseReportInput(state.Input);
            if (parsed is null)
            {
                state = state with
                {
                    Loading = false,
                    Comparison = null,
                    Error = "That does not look like a report link or report code.",
                };
                return;
            }

            CancelLocked();
            cancellation = new CancellationTokenSource();
            token = cancellation.Token;
            operationGeneration = ++generation;
            state = state with { Loading = true, Comparison = null, Error = null };
        }

        _ = CompleteAsync(operationGeneration, local.Snapshot, parsed, token);
    }

    internal void Cancel()
    {
        lock (sync)
        {
            CancelLocked();
            generation++;
            state = state with { Loading = false, Error = "Comparison canceled." };
        }
    }

    internal void ClearResult()
    {
        lock (sync)
        {
            CancelLocked();
            generation++;
            state = state with { Loading = false, Comparison = null, Error = null };
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            CancelLocked();
            generation++;
        }

        client.Dispose();
    }

    private async Task CompleteAsync(
        long operationGeneration,
        DamageEncounterSnapshot local,
        FflogsReportInput input,
        CancellationToken token)
    {
        try
        {
            var report = await client.FetchReportSummaryAsync(input.Code, token).ConfigureAwait(false);
            var requestedFightId = input.UseLastFight
                ? report.Fights.LastOrDefault(candidate => candidate.EndTime > candidate.StartTime)?.Id ?? -1
                : input.FightId;
            var fight = DamageParityComparer.SelectMatchingFight(local, report, requestedFightId);

            var events = await client.FetchAllEventsAsync(
                    new FflogsEventQuery(
                        report.Code,
                        fight.Id,
                        fight.StartTime,
                        fight.EndTime,
                        FflogsEventDataType.DamageTaken,
                        FflogsHostilityType.Enemies,
                        CacheTtl: FflogsClient.EventsCacheTtl(report, fight)),
                    token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var comparison = await Task.Run(
                    () => DamageParityComparer.Compare(local, report, fight, events),
                    token)
                .ConfigureAwait(false);
            lock (sync)
            {
                if (generation == operationGeneration && !token.IsCancellationRequested)
                {
                    cancellation?.Dispose();
                    cancellation = null;
                    state = state with
                    {
                        Loading = false,
                        Comparison = comparison,
                        Error = null,
                    };
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (sync)
            {
                if (generation == operationGeneration)
                {
                    cancellation?.Dispose();
                    cancellation = null;
                    state = state with
                    {
                        Loading = false,
                        Comparison = null,
                        Error = ex.Message,
                    };
                }
            }
        }
    }

    private void CancelLocked()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }
}
