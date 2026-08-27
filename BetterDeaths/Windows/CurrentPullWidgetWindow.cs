using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class CurrentPullWidgetWindow : ThemedWidgetWindow
{
    private readonly RecapWindow recapWindow;

    public CurrentPullWidgetWindow(Plugin plugin, RecapWindow recapWindow)
        : base(
            plugin,
            "Better Deaths Widget###BetterDeathsCurrentPullWidget",
            new Vector2(620.0f, 360.0f),
            Vector2.One)
    {
        this.recapWindow = recapWindow;
    }

    public override void Draw()
    {
        recapWindow.DrawCurrentPullWidgetContent();
        DrawResizeGrip();
    }

    public override void OnClose()
    {
        Plugin.NotifyCurrentPullWidgetClosed();
    }
}
