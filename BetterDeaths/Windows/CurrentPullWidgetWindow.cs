using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class CurrentPullWidgetWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly RecapWindow recapWindow;
    private bool stylePushed;

    public CurrentPullWidgetWindow(Plugin plugin, RecapWindow recapWindow)
        : base("Better Deaths Widget###BetterDeathsCurrentPullWidget")
    {
        this.plugin = plugin;
        this.recapWindow = recapWindow;

        Size = new Vector2(620.0f, 360.0f);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        var opacity = Math.Clamp(
            plugin.Configuration.CurrentPullWidgetBackgroundOpacity <= 0.0f
                ? Plugin.CurrentPullWidgetMaxBackgroundOpacity
                : plugin.Configuration.CurrentPullWidgetBackgroundOpacity,
            Plugin.CurrentPullWidgetMinBackgroundOpacity,
            Plugin.CurrentPullWidgetMaxBackgroundOpacity);
        ImGui.SetNextWindowBgAlpha(opacity);
        PushWidgetStyle();
    }

    public override void PostDraw()
    {
        PopWidgetStyle();
    }

    public override void Draw()
    {
        recapWindow.DrawCurrentPullWidgetContent();
        DrawBottomLeftResizeGrip(BetterDeathsThemeCatalog.GetTheme(plugin.Configuration.Theme));
    }

    public override void OnClose()
    {
        plugin.NotifyCurrentPullWidgetClosed();
    }

    private void PushWidgetStyle()
    {
        if (stylePushed)
        {
            return;
        }

        var theme = BetterDeathsThemeCatalog.GetTheme(plugin.Configuration.Theme);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.WidgetWindowBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, theme.WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, theme.WidgetTitleActiveBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, theme.WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, theme.WidgetBorderColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, theme.WidgetResizeGripColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, theme.WidgetResizeGripHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, theme.WidgetResizeGripActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.ModernTextColor);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.ModernMutedTextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        stylePushed = true;
    }

    private void PopWidgetStyle()
    {
        if (!stylePushed)
        {
            return;
        }

        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(10);
        stylePushed = false;
    }

    private static void DrawBottomLeftResizeGrip(BetterDeathsUiTheme theme)
    {
        const float gripSize = 18.0f;
        const float inset = 5.0f;
        const float lineSpacing = 4.0f;
        const float thickness = 1.3f;

        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var mousePosition = ImGui.GetIO().MousePos;
        var gripMin = new Vector2(position.X, position.Y + size.Y - gripSize);
        var gripMax = new Vector2(position.X + gripSize, position.Y + size.Y);
        var hovered = mousePosition.X >= gripMin.X &&
            mousePosition.X <= gripMax.X &&
            mousePosition.Y >= gripMin.Y &&
            mousePosition.Y <= gripMax.Y;
        var color = ImGui.GetColorU32(
            hovered
                ? ImGui.IsMouseDown(ImGuiMouseButton.Left)
                    ? theme.WidgetResizeGripActiveColor
                    : theme.WidgetResizeGripHoveredColor
                : theme.WidgetResizeGripColor);
        var origin = new Vector2(position.X + inset, position.Y + size.Y - inset);
        var drawList = ImGui.GetWindowDrawList();

        for (var lineIndex = 0; lineIndex < 3; lineIndex++)
        {
            var offset = lineIndex * lineSpacing;
            drawList.AddLine(
                new Vector2(origin.X + offset, origin.Y),
                new Vector2(origin.X, origin.Y - offset),
                color,
                thickness);
        }
    }
}
