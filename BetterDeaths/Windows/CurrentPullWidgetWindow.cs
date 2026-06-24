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
    private static readonly Vector4 WidgetWindowBackgroundColor = new(0.055f, 0.06f, 0.068f, 1.0f);
    private static readonly Vector4 WidgetTitleBackgroundColor = new(0.085f, 0.092f, 0.104f, 1.0f);
    private static readonly Vector4 WidgetTitleActiveBackgroundColor = new(0.10f, 0.11f, 0.125f, 1.0f);
    private static readonly Vector4 WidgetBorderColor = new(0.22f, 0.25f, 0.28f, 0.95f);
    private static readonly Vector4 WidgetResizeGripColor = new(0.36f, 0.92f, 0.82f, 0.30f);
    private static readonly Vector4 WidgetResizeGripHoveredColor = new(0.36f, 0.92f, 0.82f, 0.55f);
    private static readonly Vector4 WidgetResizeGripActiveColor = new(0.36f, 0.92f, 0.82f, 0.75f);

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

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WidgetWindowBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, WidgetTitleActiveBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, WidgetBorderColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, WidgetResizeGripColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, WidgetResizeGripHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, WidgetResizeGripActiveColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9.0f, 7.0f));
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
        ImGui.PopStyleColor(8);
        stylePushed = false;
    }
}
