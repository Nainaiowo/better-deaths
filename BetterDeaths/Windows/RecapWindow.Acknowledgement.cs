using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private const string CommunityAcknowledgementPopupId = "Better Deaths Thanks##CommunityAcknowledgement";
    private const string CommunityAcknowledgementIntroduction =
        "Better Deaths has finally come out of beta! We've come very far and it's thanks to all you lovely amazing individuals.";
    private const string CommunityAcknowledgementFeedback =
        "I appreciate the feedback, the countless hours I have spent with you fellow raiders improving and working on the plugin. It will continue to improve as ideas come, and remain open source for the benefit of the community.";
    private const string CommunityAcknowledgementMczub =
        "Also a special thank you to the amazing Mczub for this genius in raid tools.";
    private const string CommunityAcknowledgementFuture =
        "Me and the future (possibilities possibilities...) team will remain dedicated to providing only the best as members of the raiding community. All feedback is appreciated to continue development and give you the tools you deserve.";
    private const string CommunityAcknowledgementClosing = "Thank you all \u2665";

    private static void DrawCommunityAcknowledgementLauncher()
    {
        const string label = "Thank You \u2665";
        var width = GetThemedActionButtonWidth(label) + 12.0f;
        CenterNextItem(width);

        var pulse = GetAcknowledgementPulse();
        var buttonColor = BlendColors(
            LeadUpGoldColor,
            new Vector4(1.0f, 0.96f, 0.70f, 1.0f),
            pulse * 0.20f) with
        {
            W = 0.90f,
        };
        var hoveredColor = BlendColors(buttonColor, new Vector4(1.0f), 0.16f) with { W = 0.98f };
        var textColor = GetButtonTextColor(buttonColor, selected: true);

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BlendColors(buttonColor, ModernAccentColor, 0.20f));
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.Border, LeadUpGoldColor with { W = 0.72f + (pulse * 0.24f) });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f + (pulse * 0.7f));
        var clicked = ImGui.Button($"{label}##OpenCommunityAcknowledgement", new Vector2(width, ImGui.GetFrameHeight()));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);

        DrawAcknowledgementButtonGlow(pulse);
        if (clicked)
        {
            ImGui.OpenPopup(CommunityAcknowledgementPopupId);
        }

        DrawCommunityAcknowledgementPopup();
    }

    private static void DrawAcknowledgementButtonGlow(float pulse)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (max.X <= min.X || max.Y <= min.Y)
        {
            return;
        }

        var padding = 1.0f + pulse;
        ImGui.GetWindowDrawList().AddRect(
            min - new Vector2(padding),
            max + new Vector2(padding),
            ImGui.GetColorU32(LeadUpGoldColor with { W = 0.30f + (pulse * 0.42f) }),
            5.0f,
            ImDrawFlags.None,
            1.0f + pulse);
    }

    private static void DrawCommunityAcknowledgementPopup()
    {
        ref var viewportPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var viewportSize = ref ImGui.GetMainViewport().WorkSize;
        var popupWidth = MathF.Min(720.0f, MathF.Max(320.0f, viewportSize.X - 40.0f));
        var popupHeight = MathF.Min(540.0f, MathF.Max(340.0f, viewportSize.Y - 40.0f));
        ImGui.SetNextWindowPos(
            viewportPosition + (viewportSize * 0.5f),
            ImGuiCond.Always,
            new Vector2(0.5f));
        ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight), ImGuiCond.Always);

        var pulse = GetAcknowledgementPulse();
        var border = BlendColors(
            LeadUpGoldColor,
            new Vector4(1.0f, 0.97f, 0.76f, 1.0f),
            pulse * 0.28f) with
        {
            W = 0.78f + (pulse * 0.20f),
        };
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
        ImGui.PushStyleColor(ImGuiCol.Border, border);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16.0f, 14.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2.0f + (pulse * 0.8f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f, 9.0f));
        if (!ImGui.BeginPopup(
                CommunityAcknowledgementPopupId,
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(2);
            return;
        }

        DrawAcknowledgementPopupBorder(border, pulse);
        ImGui.TextColored(GetChangelogHighlightTextColor(), "A Note from Nai");
        ImGui.Separator();

        var footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + 4.0f;
        if (ImGui.BeginChild(
                "##CommunityAcknowledgementMessage",
                new Vector2(0.0f, -footerHeight),
                false,
                ImGuiWindowFlags.None))
        {
            ImGui.TextWrapped(CommunityAcknowledgementIntroduction);
            ImGui.Spacing();
            ImGui.TextWrapped(CommunityAcknowledgementFeedback);
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, GetChangelogHighlightTextColor());
            ImGui.TextWrapped(CommunityAcknowledgementMczub);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.TextWrapped(CommunityAcknowledgementFuture);
            ImGui.Spacing();
            DrawCenteredOrWrappedText(CommunityAcknowledgementClosing, GetChangelogHighlightTextColor());
        }

        ImGui.EndChild();
        ImGui.Separator();
        const float closeButtonWidth = 96.0f;
        CenterNextItem(closeButtonWidth);
        if (DrawThemedActionButton("Close", "CloseCommunityAcknowledgement", closeButtonWidth))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
    }

    private static void DrawAcknowledgementPopupBorder(Vector4 border, float pulse)
    {
        var start = ImGui.GetWindowPos();
        var end = start + ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            start + new Vector2(1.0f),
            end - new Vector2(1.0f),
            ImGui.GetColorU32(border),
            7.0f,
            ImDrawFlags.None,
            1.5f + (pulse * 1.2f));

        var width = MathF.Max(0.0f, end.X - start.X);
        var shimmerWidth = MathF.Min(120.0f, MathF.Max(36.0f, width * 0.20f));
        var travel = (MathF.Sin((float)ImGui.GetTime() * 1.15f) + 1.0f) * 0.5f;
        var shimmerX = start.X + 8.0f + (MathF.Max(0.0f, width - shimmerWidth - 16.0f) * travel);
        drawList.AddLine(
            new Vector2(shimmerX, start.Y + 1.5f),
            new Vector2(shimmerX + shimmerWidth, start.Y + 1.5f),
            ImGui.GetColorU32(border with { W = 0.55f + (pulse * 0.30f) }),
            2.0f);
    }

    private static float GetAcknowledgementPulse()
    {
        return (MathF.Sin((float)ImGui.GetTime() * 2.4f) + 1.0f) * 0.5f;
    }
}
