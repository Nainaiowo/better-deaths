using BetterDeaths.PartyCooldowns;
using Dalamud.Bindings.ImGui;
using System;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly PartyCooldownPerformance[] partyCooldownPerformanceView = new PartyCooldownPerformance[3];
    private DateTime nextPartyCooldownPerformanceViewAt;

    private void DrawPartyCooldownPerformanceDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Party cooldown performance"))
            return;
        var enabled = configuration.DebugProfilePartyCooldowns;
        if (DrawThemedCheckbox("Measure party cooldown CPU time", ref enabled))
        {
            plugin.SetPartyCooldownProfiling(enabled);
            nextPartyCooldownPerformanceViewAt = default;
        }
        if (DateTime.UtcNow >= nextPartyCooldownPerformanceViewAt)
        {
            for (var index = 0; index < partyCooldownPerformanceView.Length; index++)
                partyCooldownPerformanceView[index] = plugin.GetPartyCooldownPerformance((PartyCooldownPhase)index);
            nextPartyCooldownPerformanceViewAt = DateTime.UtcNow.AddSeconds(1);
        }
        if (!ImGui.BeginTable("##PartyCooldownPerformance", 6, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("Work");
        ImGui.TableSetupColumn("Samples");
        ImGui.TableSetupColumn("Average ms");
        ImGui.TableSetupColumn("Recent p99 ms");
        ImGui.TableSetupColumn("Maximum ms");
        ImGui.TableSetupColumn("Bytes / call");
        ImGui.TableHeadersRow();
        for (var index = 0; index < partyCooldownPerformanceView.Length; index++)
        {
            var metric = partyCooldownPerformanceView[index];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(((PartyCooldownPhase)index).ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(metric.Samples.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(metric.AverageMs.ToString("F4"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(metric.RecentP99Ms.ToString("F4"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(metric.MaximumMs.ToString("F4"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(metric.BytesPerCall.ToString("F1"));
        }
        ImGui.EndTable();
        var update = partyCooldownPerformanceView[(int)PartyCooldownPhase.Update];
        if (update.Samples > 0)
        {
            double total = 0;
            foreach (var metric in partyCooldownPerformanceView)
                total += metric.AverageMs * metric.Samples;
            ImGui.TextUnformatted($"Mean combined CPU / update: {total / update.Samples:F4} ms");
        }
        ImGui.TextDisabled("CPU only; GPU cost is not measured.");
    }
}
