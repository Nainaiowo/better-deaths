using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private static void DrawTileInsertionMarker(Vector2 start, Vector2 end, bool after)
    {
        var x = after ? end.X : start.X;
        var draw = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(ModernAccentColor);
        draw.AddLine(new(x, start.Y), new(x, end.Y), color, 2);
        draw.AddLine(new(x - 3, start.Y), new(x + 3, start.Y), color, 2);
        draw.AddLine(new(x - 3, end.Y), new(x + 3, end.Y), color, 2);
    }
}
