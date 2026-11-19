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

            ImGui.Spacing();
            ImGui.TextColored(ModernAccentColor, "Food marker layout");
            var placementIconSize = configuration.GetPartyCooldownPlacement().IconSize;
            var markerIconSize = configuration.PartyFoodStatusIconSize ?? placementIconSize;
            if (ImGui.SliderFloat("Food icon size##PartyFoodMarkerSize", ref markerIconSize,
                    FoodCheckMarkerPolicy.MinimumIconSize, FoodCheckMarkerPolicy.MaximumIconSize, "%.0f px"))
            {
                configuration.PartyFoodStatusIconSize = markerIconSize;
                foodCheckSavePending = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                foodCheckSavePending = true;
            DrawSettingsTooltip("Set the Well Fed marker size independently from cooldown icons.");

            var markerOffsetX = configuration.PartyFoodStatusIconOffsetX;
            if (ImGui.SliderFloat("Food icon X offset##PartyFoodMarkerOffsetX", ref markerOffsetX,
                    -FoodCheckMarkerPolicy.MaximumOffset, FoodCheckMarkerPolicy.MaximumOffset, "%.0f px"))
            {
                configuration.PartyFoodStatusIconOffsetX = markerOffsetX;
                foodCheckSavePending = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                foodCheckSavePending = true;
            DrawSettingsTooltip("Move the Well Fed marker horizontally from its cooldown-layout slot. Negative values move left; positive values move right.");

            var markerOffsetY = configuration.PartyFoodStatusIconOffsetY;
            if (ImGui.SliderFloat("Food icon Y offset##PartyFoodMarkerOffsetY", ref markerOffsetY,
                    -FoodCheckMarkerPolicy.MaximumOffset, FoodCheckMarkerPolicy.MaximumOffset, "%.0f px"))
            {
                configuration.PartyFoodStatusIconOffsetY = markerOffsetY;
                foodCheckSavePending = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                foodCheckSavePending = true;
            DrawSettingsTooltip("Move the Well Fed marker vertically from its cooldown-layout slot. Negative values move up; positive values move down.");

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

                var heightPercent = configuration.PartyFoodPlateTintHeight * 100.0f;
                if (ImGui.SliderFloat("Plate tint height##PartyFoodTint", ref heightPercent, 20, 100, "%.0f%%"))
                {
                    configuration.PartyFoodPlateTintHeight = heightPercent / 100.0f;
                    foodCheckSavePending = true;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    foodCheckSavePending = true;
                DrawSettingsTooltip("Set how much of the party row's height is tinted. Use the Y offset below to move the tint without changing its size.");

                var offsetY = configuration.PartyFoodPlateTintOffsetY;
                if (ImGui.SliderFloat("Plate tint Y offset##PartyFoodTintOffsetY", ref offsetY,
                        -FoodCheckTintPolicy.MaximumOffsetY, FoodCheckTintPolicy.MaximumOffsetY, "%.0f px"))
                {
                    configuration.PartyFoodPlateTintOffsetY = offsetY;
                    foodCheckSavePending = true;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    foodCheckSavePending = true;
                DrawSettingsTooltip("Negative values move the tint up; positive values move it down. Zero centers it on the party row.");
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Marker side follows Party Cooldowns > Layout; size and X/Y offsets are independent.");
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
            var (topInset, tintHeight) = FoodCheckTintPolicy.GetVerticalBounds(
                max.Y - min.Y, configuration.PartyFoodPlateTintHeight, configuration.PartyFoodPlateTintOffsetY);
            var tintMin = new Vector2(min.X, min.Y + topInset);
            var tintMax = new Vector2(max.X, tintMin.Y + tintHeight);
            draw.AddRectFilled(tintMin, tintMax, ImGui.GetColorU32(GetFoodCheckStateColor(displayState) with
            {
                W = opacity,
            }), 4);
            draw.AddRect(tintMin, tintMax, ImGui.GetColorU32(GetFoodCheckStateColor(displayState) with { W = opacity }), 4,
                ImDrawFlags.None, 1);
        }
        else
            draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(.24f, .27f, .32f, .9f)), 4);

        var markerSize = FoodCheckMarkerPolicy.NormalizeIconSize(
            configuration.PartyFoodStatusIconSize ?? configuration.GetPartyCooldownPlacement().IconSize,
            configuration.GetPartyCooldownPlacement().IconSize);
        var markerLeft = configuration.PartyCooldownSide == PartyCooldownSide.Left;
        var markerPosition = (markerLeft
            ? new Vector2(min.X + 8, min.Y + (height - markerSize) * .5f)
            : new Vector2(max.X - markerSize - 8, min.Y + (height - markerSize) * .5f)) +
            new Vector2(FoodCheckMarkerPolicy.NormalizeOffset(configuration.PartyFoodStatusIconOffsetX),
                FoodCheckMarkerPolicy.NormalizeOffset(configuration.PartyFoodStatusIconOffsetY));
        var jobPosition = markerLeft
            ? new Vector2(min.X + markerSize + 18, min.Y + (height - 20) * .5f)
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
        var markerReserve = !stateVisible ? 10 : markerLeft ? 43 : MathF.Max(43, markerSize + 16);
        var detailPosition = new Vector2(MathF.Max(namePosition.X + 105, max.X - detailWidth - markerReserve), min.Y + 12);
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

        var rowMin = viewportOffset + new Vector2(rowNode->ScreenX, rowNode->ScreenY);
        var (topInset, tintHeight) = FoodCheckTintPolicy.GetVerticalBounds(
            rowHeight, configuration.PartyFoodPlateTintHeight, configuration.PartyFoodPlateTintOffsetY);
        var min = rowMin + new Vector2(0, topInset);
        var max = new Vector2(rowMin.X + rowWidth, min.Y + tintHeight);
        var color = GetFoodCheckStateColor(state);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(color with
        {
            W = Math.Clamp(configuration.PartyFoodPlateTintOpacity, .04f, .40f),
        }), 3 * scale);
    }
}
