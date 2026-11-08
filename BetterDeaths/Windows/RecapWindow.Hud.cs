using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private enum HudSettingsView { DamageMeter, DeathWidget, PartyCooldowns }
    private HudSettingsView hudSettingsView;
    private GuideTarget? hudGuideTarget;

    private void DrawHudSettingsPage()
    {
        var target = guideActive && OnboardingGuide.Steps[guideStepIndex].Section == GuideSection.Widgets
            ? OnboardingGuide.Steps[guideStepIndex].Target : (GuideTarget?)null;
        if (target != hudGuideTarget)
        {
            if (target is GuideTarget.DeathWidget)
                hudSettingsView = HudSettingsView.DeathWidget;
            else if (OnboardingGuide.IsPartyCooldownTarget(target))
            {
                hudSettingsView = HudSettingsView.PartyCooldowns;
                if (target == GuideTarget.PartyCooldowns)
                    cooldownRequestedJob = cooldownPreviewJob;
            }
            else if (target is GuideTarget.MeterWidget or GuideTarget.MeterColumns or GuideTarget.MeterEncounters or GuideTarget.MeterPreview)
                hudSettingsView = HudSettingsView.DamageMeter;
            hudGuideTarget = target;
        }
        var views = Enum.GetValues<HudSettingsView>();
        var preferredWidth = views.Max(view => GetThemedActionButtonWidth(GetHudViewLabel(view)));
        var navigation = HudLayoutPolicy.NavigationLayout(ImGui.GetContentRegionAvail().X, preferredWidth,
            ImGui.GetStyle().ItemSpacing.X, views.Length, ModernWorkspaceHorizontalPadding);
        var columns = navigation.Columns;
        var width = navigation.ButtonWidth;
        var left = ImGui.GetCursorPosX();
        var height = MathF.Max(24, ImGui.GetFrameHeight());
        for (var index = 0; index < views.Length; index++)
        {
            if (index % columns != 0)
                ImGui.SameLine();
            else
                ImGui.SetCursorPosX(left + navigation.Inset);
            var selected = hudSettingsView == views[index];
            var border = selected ? GetButtonTextColor(ModernNavButtonSelectedColor, selected: true) : ModernMutedTextColor;
            ImGui.PushStyleColor(ImGuiCol.Border, border with { W = .8f });
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f);
            var fullLabel = GetHudViewLabel(views[index]);
            var label = GetThemedActionButtonWidth(fullLabel) <= width ? fullLabel : views[index] switch
            {
                HudSettingsView.DamageMeter => "DPS",
                HudSettingsView.DeathWidget => "Deaths",
                _ => "Cooldowns",
            };
            bool clicked;
            if (GetThemedActionButtonWidth(label) > width)
            {
                using var font = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
                var icon = views[index] switch
                {
                    HudSettingsView.DamageMeter => FontAwesomeIcon.ChartBar,
                    HudSettingsView.DeathWidget => FontAwesomeIcon.Skull,
                    _ => FontAwesomeIcon.Clock,
                };
                clicked = DrawSegmentedButton(icon.ToIconString(), $"HudSettings{views[index]}", selected, width, height);
            }
            else
                clicked = DrawSegmentedButton(label, $"HudSettings{views[index]}", selected, width, height);
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                SetThemedTooltip(fullLabel);
            if (clicked)
                hudSettingsView = views[index];
        }
        ImGui.Dummy(new Vector2(1, 8));
        switch (hudSettingsView)
        {
            case HudSettingsView.DamageMeter:
                DrawDamageMeterWorkspace();
                break;
            case HudSettingsView.DeathWidget:
                DrawReviewPanel("##HudDeathWidget", Vector2.Zero, DrawWidgetTab, guideTarget: GuideTarget.DeathWidget);
                break;
            case HudSettingsView.PartyCooldowns:
                DrawReviewPanel("##HudPartyCooldowns", Vector2.Zero, DrawPartyCooldownSettings);
                break;
        }
    }

    private static string GetHudViewLabel(HudSettingsView view) => view switch
    {
        HudSettingsView.DamageMeter => "DPS Meter",
        HudSettingsView.DeathWidget => "Death Window",
        _ => "Party Cooldowns",
    };

    private static void DrawHudAppearanceSettings(string id, Action drawOpacity, Action drawIconSize)
    {
        var minimum = MathF.Max(400, ImGui.CalcTextSize("Background opacity").X * 2 + ImGui.GetStyle().CellPadding.X * 4 + 24);
        if (ImGui.GetContentRegionAvail().X >= minimum)
        {
            if (ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn();
                drawOpacity();
                ImGui.TableNextColumn();
                drawIconSize();
                ImGui.EndTable();
            }
        }
        else
        {
            drawOpacity();
            ImGui.Spacing();
            drawIconSize();
        }
    }
}
