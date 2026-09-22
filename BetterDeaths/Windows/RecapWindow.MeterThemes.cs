using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private void DrawCatalogDamageMeterHeader(string state, double duration, double dps, bool showControls)
    {
        var profile = MeterThemeStyle.Profile(activeTheme.Id);
        var scale = ImGui.GetTextLineHeight() / 17;
        var padding = 6 * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X - ReviewPaneHorizontalPadding);
        var timer = FormatDamageMeterDuration(duration);
        var totalDps = $"{FormatDamageMeterValue(dps)} DPS";
        var timerWidth = MeasureWidgetSummaryValue(timer, MeterThemeStyle.ValueScale).X;
        var dpsWidth = MeasureWidgetSummaryValue(totalDps, MeterThemeStyle.ValueScale).X;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var hasControls = showControls && plugin.IsDamageMeterManualResetAvailable;
        var controlsWidth = hasControls ? ImGui.GetFrameHeight() + spacing.X : 0;
        var layout = MeterThemeStyle.Layout(width, scale, ImGui.CalcTextSize(state).X, timerWidth, dpsWidth, controlsWidth);
        var surface = MeterThemeStyle.Summary(activeTheme.Id, activeTheme.WidgetWindowBackgroundColor);
        var text = WidgetSurfacePolicy.ReadableText(ModernTextColor, surface);
        var accent = WidgetSurfacePolicy.ReadableText(ModernAccentColor, surface);
        var secondary = WidgetSurfacePolicy.ReadableText(LeadUpGoldColor, surface);
        var draw = ImGui.GetWindowDrawList();

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, padding));
        DrawDamageMeterHeading(state, showControls, text,
            ReviewPaneHorizontalPadding + (layout.InlineHeading ? layout.MetricsWidth + 24 * scale : 0));
        var headingBottom = ImGui.GetCursorScreenPos().Y - spacing.Y;
        var metrics = layout.InlineHeading
            ? origin + new Vector2(width - layout.MetricsWidth, padding)
            : new Vector2(origin.X, headingBottom + 3 * scale);
        var timerHeight = DrawWidgetSummaryValue(metrics, layout.StackMetrics ? width : timerWidth, timer,
            MeterThemeStyle.ValueScale, Vector4.One, outline: true);
        var dpsPosition = layout.StackMetrics
            ? metrics + new Vector2(0, timerHeight + 3 * scale)
            : metrics + new Vector2(timerWidth + 12 * scale, 0);
        var dpsHeight = DrawWidgetSummaryValue(dpsPosition, layout.StackMetrics ? width : dpsWidth, totalDps,
            MeterThemeStyle.ValueScale, profile.TimerFirst ? secondary : accent);
        var height = MathF.Max(32 * scale,
            MathF.Max(headingBottom, MathF.Max(metrics.Y + timerHeight, dpsPosition.Y + dpsHeight)) - origin.Y + padding);

        draw.ChannelsSetCurrent(0);
        var left = origin - new Vector2(ReviewPaneHorizontalPadding, 0);
        var right = origin + new Vector2(width + ReviewPaneHorizontalPadding, height);
        var opacity = GetDamageMeterWidgetBackgroundOpacity();
        draw.AddRectFilled(left, right, ImGui.GetColorU32(surface with { W = opacity }));
        MeterHeaderBackgroundRenderer.Draw(activeTheme.Id, left, right - left, accent, secondary,
            configuration.AnimateStyleThemes, configuration.StyleThemeEffectStrength, opacity);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(origin);
        // Advance directly to the table: Dummy's normal item spacing would leave an empty separator band.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, spacing with { Y = 0 });
        ImGui.Dummy(new Vector2(width, height));
        ImGui.PopStyleVar();
    }
}
