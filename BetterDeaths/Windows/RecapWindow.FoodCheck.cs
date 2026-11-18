using BetterDeaths.PartyCooldowns;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly record struct FoodCheckPreviewRow(
        string Name,
        uint ClassJobId,
        FoodCheckIndicatorState State,
        PartyFoodStatusSample Status);

    private void DrawFoodCheckSettings()
    {
        var start = ImGui.GetCursorScreenPos();
        DrawModernSectionTitle("Food Check", "Party food status at a glance");
        DrawSubtleSeparator();
        ImGui.Spacing();
        var visible = configuration.ShowPartyFoodStatus;
        if (DrawThemedCheckbox("Show Well Fed status", ref visible))
            plugin.SetPartyFoodStatusVisibility(visible);
        DrawSettingsTooltip("Add a food status marker beside visible party members. Status is sampled once per second only while you are out of combat inside an instance.");

        if (visible)
        {
            ImGui.Spacing();
            var warningMinutes = configuration.PartyFoodWarningMinutes;
            if (ImGui.SliderInt("Warn when under##PartyFoodWarning", ref warningMinutes, 1, 60, "%d min"))
            {
                configuration.PartyFoodWarningMinutes = warningMinutes;
                foodCheckSavePending = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                foodCheckSavePending = true;
            DrawSettingsTooltip("Food below this remaining time is marked amber. Food at or above it stays in the healthy state.");

            var warningsOnly = configuration.PartyFoodWarningsOnly;
            if (DrawThemedCheckbox("Only show warnings", ref warningsOnly))
            {
                configuration.PartyFoodWarningsOnly = warningsOnly;
                foodCheckSavePending = true;
            }
            DrawSettingsTooltip("Hide the marker for healthy food. Missing food and food below the warning threshold remain visible.");

            var tintPlate = configuration.PartyFoodTintPartyPlate;
            if (DrawThemedCheckbox("Tint party plates", ref tintPlate))
            {
                configuration.PartyFoodTintPartyPlate = tintPlate;
                foodCheckSavePending = true;
            }
            DrawSettingsTooltip("Apply a translucent status color over each visible party row. This is optional and disabled by default.");
            if (tintPlate)
            {
                var opacityPercent = configuration.PartyFoodPlateTintOpacity * 100.0f;
                if (ImGui.SliderFloat("Plate tint strength##PartyFoodTint", ref opacityPercent, 4, 40, "%.0f%%"))
                {
                    configuration.PartyFoodPlateTintOpacity = opacityPercent / 100.0f;
                    foodCheckSavePending = true;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    foodCheckSavePending = true;
                DrawSettingsTooltip("Keep the tint subtle so party names, HP bars, and status icons stay readable.");
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Marker position and size follow Party Cooldowns > Layout.");
            ImGui.TextDisabled("Green means active, amber means refresh soon, and red means no food was detected.");
        }

        ImGui.Spacing();
        DrawFoodCheckPreview();
        RegisterGuideTarget(GuideTarget.FoodCheck, start,
            new Vector2(start.X + ImGui.GetContentRegionAvail().X, ImGui.GetCursorScreenPos().Y));

        if (foodCheckSavePending)
        {
            foodCheckSavePending = false;
            plugin.SaveConfiguration();
        }
    }

    private bool foodCheckSavePending;

    private void DrawFoodCheckPreview()
    {
        ImGui.TextColored(ModernAccentColor, "Preview");
        ImGui.TextDisabled("Example party rows show how each captured state appears.");
        var width = MathF.Max(220, ImGui.GetContentRegionAvail().X);
        const float rowHeight = 42;
        var rows = new[]
        {
            new FoodCheckPreviewRow("A. D.", 19, FoodCheckIndicatorState.Active,
                new(true, 2538)),
            new FoodCheckPreviewRow("M. C.", 24, FoodCheckIndicatorState.Expiring,
                new(true, 267)),
            new FoodCheckPreviewRow("R. T.", 32, FoodCheckIndicatorState.Missing,
                new(false, 0)),
            new FoodCheckPreviewRow("D. R.", 25, FoodCheckIndicatorState.Hidden,
                new(false, 0)),
        };
        if (ImGui.BeginChild("##FoodCheckVisualPreview", new Vector2(0, rowHeight * rows.Length + 12), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var draw = ImGui.GetWindowDrawList();
            var start = ImGui.GetCursorScreenPos();
            for (var index = 0; index < rows.Length; index++)
                DrawFoodCheckPreviewRow(draw, start + new Vector2(0, index * rowHeight), width, rowHeight,
                    rows[index], index);
            ImGui.Dummy(new Vector2(width, rowHeight * rows.Length));
        }
        ImGui.EndChild();
        ImGui.TextDisabled("The last row is unavailable data, not a missing-food alert.");
    }

    private void DrawFoodCheckPreviewRow(ImDrawListPtr draw, Vector2 position, float width, float height,
        FoodCheckPreviewRow row, int index)
    {
        var min = position + new Vector2(2, 2);
        var max = position + new Vector2(width - 2, height - 2);
        var background = index % 2 == 0
            ? new Vector4(.075f, .085f, .105f, .98f)
            : new Vector4(.095f, .105f, .125f, .98f);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(background), 4);

        var displayState = FoodCheckIndicatorPolicy.ForDisplay(row.State, configuration.PartyFoodWarningsOnly);
        var stateVisible = displayState != FoodCheckIndicatorState.Hidden;
        if (configuration.PartyFoodTintPartyPlate && stateVisible)
        {
            var opacity = Math.Clamp(configuration.PartyFoodPlateTintOpacity, .04f, .40f);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(GetFoodCheckStateColor(displayState) with
            {
                W = opacity,
            }), 4);
            draw.AddRect(min, max, ImGui.GetColorU32(GetFoodCheckStateColor(displayState) with { W = opacity }), 4,
                ImDrawFlags.None, 1);
        }
        else
            draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(.24f, .27f, .32f, .9f)), 4);

        var markerSize = 25.0f;
        var markerLeft = configuration.PartyCooldownSide == PartyCooldownSide.Left;
        var markerPosition = markerLeft
            ? new Vector2(min.X + 8, min.Y + (height - markerSize) * .5f)
            : new Vector2(max.X - markerSize - 8, min.Y + (height - markerSize) * .5f);
        var jobPosition = markerLeft
            ? new Vector2(min.X + 42, min.Y + (height - 20) * .5f)
            : new Vector2(min.X + 10, min.Y + (height - 20) * .5f);
        DrawPartyCooldownIcon(draw, jobPosition, 20, GetClassJobIconId(row.ClassJobId), Vector4.One);

        var namePosition = jobPosition + new Vector2(27, 2);
        draw.AddText(namePosition, ImGui.GetColorU32(ImGuiCol.Text), row.Name);
        var barMin = namePosition + new Vector2(0, 18);
        var barMax = barMin + new Vector2(MathF.Min(88, MathF.Max(54, width * .15f)), 5);
        draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(new Vector4(.13f, .15f, .18f, 1)), 2);
        draw.AddRectFilled(barMin, new Vector2(barMin.X + (barMax.X - barMin.X) * .82f, barMax.Y),
            ImGui.GetColorU32(new Vector4(.36f, .72f, .45f, 1)), 2);

        var detail = row.State switch
        {
            FoodCheckIndicatorState.Active when configuration.PartyFoodWarningsOnly => "Well Fed · healthy (hidden)",
            FoodCheckIndicatorState.Active => $"Well Fed · {FoodCheckFormatter.FormatRemainingSeconds(row.Status.RemainingSeconds)}",
            FoodCheckIndicatorState.Expiring => $"Refresh soon · {FoodCheckFormatter.FormatRemainingSeconds(row.Status.RemainingSeconds)}",
            FoodCheckIndicatorState.Missing => "No food detected",
            _ => "Status not available",
        };
        var detailWidth = ImGui.CalcTextSize(detail).X;
        var detailPosition = new Vector2(MathF.Max(namePosition.X + 105, max.X - detailWidth - (stateVisible ? 43 : 10)), min.Y + 12);
        draw.AddText(detailPosition, ImGui.GetColorU32(row.State == FoodCheckIndicatorState.Hidden
            ? ModernMutedTextColor
            : GetFoodCheckStateColor(row.State)), detail);

        if (!stateVisible)
            return;
        DrawPartyFoodStatus(draw, markerPosition, markerSize, displayState, row.Status);
        if (ImGui.IsMouseHoveringRect(markerPosition, markerPosition + new Vector2(markerSize)))
            ImGui.SetTooltip("Example only. Live timers come from the current party status.");
    }

    private static Vector4 GetFoodCheckStateColor(FoodCheckIndicatorState state) => state switch
    {
        FoodCheckIndicatorState.Expiring => new Vector4(1.0f, .67f, .20f, 1),
        FoodCheckIndicatorState.Missing => new Vector4(.98f, .25f, .30f, 1),
        FoodCheckIndicatorState.Active => new Vector4(.40f, .88f, .73f, 1),
        _ => new Vector4(.52f, .56f, .62f, 1),
    };

    private unsafe void DrawFoodCheckPlateTint(ImDrawListPtr draw, AtkResNode* rowNode, Vector2 viewportOffset,
        float scale, FoodCheckIndicatorState state)
    {
        if (!configuration.PartyFoodTintPartyPlate || rowNode is null || state == FoodCheckIndicatorState.Hidden)
            return;

        var rowWidth = MathF.Max(0, rowNode->Width * scale);
        var rowHeight = MathF.Max(0, rowNode->Height * scale);
        if (!float.IsFinite(rowWidth) || !float.IsFinite(rowHeight) || rowWidth is < 100 or > 1400 || rowHeight is < 20 or > 120)
            return;

        var min = viewportOffset + new Vector2(rowNode->ScreenX, rowNode->ScreenY);
        var max = min + new Vector2(rowWidth, rowHeight);
        var color = GetFoodCheckStateColor(state);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(color with
        {
            W = Math.Clamp(configuration.PartyFoodPlateTintOpacity, .04f, .40f),
        }), 3 * scale);
    }
}
