using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class CurrentPullWidgetWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly RecapWindow recapWindow;

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
    }

    public override void Draw()
    {
        recapWindow.DrawCurrentPullWidgetContent();
    }

    public override void OnClose()
    {
        plugin.NotifyCurrentPullWidgetClosed();
    }
}
