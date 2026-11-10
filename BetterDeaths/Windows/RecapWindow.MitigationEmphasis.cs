using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private static readonly Vector2[] MitigationGlowOffsets = [new(-.8f, 0), new(.8f, 0), new(0, -.8f), new(0, .8f)];

    private static void DrawEmphasizedMitigationText(string text)
    {
        var pulse = MitigationReviewPresentation.ExpiredPulse(ImGui.GetTime(), Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion);
        var color = BlendColors(ModernTextColor, LeadUpGoldColor, .45f + .15f * pulse) with { W = 1 };
        var glow = LeadUpGoldColor with { W = .06f + .07f * pulse };
        var size = ImGui.CalcTextSize(text);
        if (ImGui.IsRectVisible(size))
        {
            var position = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            var glowColor = ImGui.GetColorU32(glow);
            foreach (var offset in MitigationGlowOffsets)
                draw.AddText(position + offset, glowColor, text);
        }
        ImGui.TextColored(color, text);
    }
}
