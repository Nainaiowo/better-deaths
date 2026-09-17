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
        var padding = 12 * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X - padding);
        var first = profile.TimerFirst ? FormatDamageMeterDuration(duration) : FormatDamageMeterValue(dps);
        var second = profile.TimerFirst ? FormatDamageMeterValue(dps) : FormatDamageMeterDuration(duration);
        var firstLabel = profile.TimerFirst ? "Duration" : "Total DPS";
        var secondLabel = profile.TimerFirst ? "Total DPS" : "Duration";
        var firstWidth = MathF.Max(MeasureWidgetSummaryValue(first, MeterThemeStyle.PrimaryScale).X, ImGui.CalcTextSize(firstLabel).X);
        var secondWidth = MathF.Max(MeasureWidgetSummaryValue(second, MeterThemeStyle.SecondaryScale).X, ImGui.CalcTextSize(secondLabel).X);
        var layout = MeterThemeStyle.Layout(width, scale, firstWidth, secondWidth);
        var surface = MeterThemeStyle.Summary(activeTheme.Id, activeTheme.WidgetWindowBackgroundColor);
        var text = WidgetSurfacePolicy.ReadableText(ModernTextColor, surface);
        var muted = WidgetSurfacePolicy.ReadableText(ModernMutedTextColor, surface);
        var accent = WidgetSurfacePolicy.ReadableText(ModernAccentColor, surface);
        var secondary = WidgetSurfacePolicy.ReadableText(LeadUpGoldColor, surface);
        var draw = ImGui.GetWindowDrawList();

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, padding));
        DrawDamageMeterHeading(state, showControls, text, padding);
        var metrics = ImGui.GetCursorScreenPos() + new Vector2(0, 5 * scale);
        var firstHeight = DrawWidgetSummaryMetric(metrics, layout.TextWidth, firstLabel, first, MeterThemeStyle.PrimaryScale, muted, accent);
        var secondPosition = layout.StackMetrics
            ? metrics + new Vector2(0, firstHeight + 10 * scale)
            : metrics + new Vector2(profile.TimerFirst ? firstWidth + 40 * scale : MathF.Max(firstWidth + 40 * scale, layout.TextWidth - secondWidth - 12 * scale), 0);
        var secondHeight = DrawWidgetSummaryMetric(secondPosition, layout.StackMetrics ? layout.TextWidth : secondWidth,
            secondLabel, second, MeterThemeStyle.SecondaryScale, muted, secondary);
        if (!layout.StackMetrics)
        {
            var divider = secondPosition + new Vector2(-20 * scale, 3 * scale);
            draw.AddLine(divider, divider + new Vector2(0, MathF.Max(firstHeight, secondHeight) - 6 * scale),
                ImGui.GetColorU32(secondary with { W = .24f }));
        }
        var height = MathF.Max(metrics.Y + firstHeight, secondPosition.Y + secondHeight) - origin.Y + padding;

        draw.ChannelsSetCurrent(0);
        var left = origin - new Vector2(ReviewPaneHorizontalPadding, 0);
        var right = origin + new Vector2(width + padding, height);
        var opacity = GetDamageMeterWidgetBackgroundOpacity();
        draw.AddRectFilled(left, right, ImGui.GetColorU32(surface with { W = opacity }));
        MeterHeaderBackgroundRenderer.Draw(activeTheme.Id, left, right - left, accent, secondary,
            configuration.AnimateStyleThemes, configuration.StyleThemeEffectStrength, opacity);
        draw.AddLine(new Vector2(left.X, right.Y), right,
            ImGui.GetColorU32(MeterThemeStyle.ColumnHeader(activeTheme.Id, activeTheme.WidgetWindowBackgroundColor) with { W = opacity }), 2 * scale);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }
}
