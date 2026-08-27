using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class DamageMeterWidgetWindow : ThemedWidgetWindow
{
    private readonly RecapWindow recapWindow;

    public DamageMeterWidgetWindow(Plugin plugin, RecapWindow recapWindow)
        : base(
            plugin,
            "Better Deaths Meter###BetterDeathsDamageMeterWidget",
            new Vector2(520.0f, 320.0f),
            new Vector2(400.0f, 180.0f))
    {
        this.recapWindow = recapWindow;
    }

    public override void Draw()
    {
        recapWindow.DrawDamageMeterWidgetContent();
        DrawResizeGrip();
    }

    public override void OnClose()
    {
        Plugin.NotifyDamageMeterWidgetClosed();
    }
}
