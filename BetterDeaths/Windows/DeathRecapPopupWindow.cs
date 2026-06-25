using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class DeathRecapPopupWindow : Window, IDisposable
{
    private static readonly TimeSpan PopupLifetime = TimeSpan.FromSeconds(30);
    private static readonly Vector4 PopupWindowBgColor = new(0.055f, 0.06f, 0.068f, 1.0f);
    private static readonly Vector4 PopupTitleBgColor = new(0.085f, 0.092f, 0.104f, 1.0f);
    private static readonly Vector4 PopupButtonColor = new(0.04f, 0.34f, 0.32f, 1.0f);
    private static readonly Vector4 PopupButtonHoveredColor = new(0.06f, 0.46f, 0.43f, 1.0f);
    private static readonly Vector4 PopupButtonActiveColor = new(0.08f, 0.55f, 0.50f, 1.0f);
    private static readonly Vector2 DefaultSize = new(240.0f, 82.0f);
    private const float PopupBackgroundOpacity = 0.85f;
    private readonly Plugin plugin;
    private readonly RecapWindow recapWindow;
    private PendingDeath? pendingDeath;
    private Vector2? lastKnownPosition;
    private bool applySavedPositionOnNextDraw;
    private bool stylePushed;

    public DeathRecapPopupWindow(Plugin plugin, RecapWindow recapWindow)
        : base(
            "Better Deaths###BetterDeathsDeathRecapPopup",
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        this.recapWindow = recapWindow;

        Size = DefaultSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        PositionCondition = ImGuiCond.FirstUseEver;

        ref var viewportPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var viewportSize = ref ImGui.GetMainViewport().WorkSize;
        Position = viewportPosition + ((viewportSize - (DefaultSize * ImGuiHelpers.GlobalScale)) * 0.5f);
    }

    public void Dispose()
    {
        SaveLastKnownPosition();
    }

    public void DisplayDeath(PartyDeathRecord death)
    {
        pendingDeath = new PendingDeath(
            death.SeenAtUtc,
            death.SeenAtUtc.Ticks,
            Plugin.GetMemberKeyHash(death.MemberKey),
            death.MemberName,
            death.ClassJobName);
        applySavedPositionOnNextDraw = true;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        ApplyPopupPosition();
        ImGui.SetNextWindowBgAlpha(PopupBackgroundOpacity);
        PushPopupStyle();
    }

    public override void PostDraw()
    {
        PopPopupStyle();
    }

    public override void Draw()
    {
        lastKnownPosition = ImGui.GetWindowPos();

        if (pendingDeath is not { } death ||
            recapWindow.IsOpen ||
            DateTime.UtcNow - death.SeenAtUtc >= PopupLifetime)
        {
            ClosePopup();
            return;
        }

        var remainingSeconds = Math.Max(0, (int)Math.Ceiling((PopupLifetime - (DateTime.UtcNow - death.SeenAtUtc)).TotalSeconds));
        var label = $"Open death recap ({remainingSeconds}s)";
        if (ImGui.Button(label, new Vector2(-1.0f, -1.0f)))
        {
            if (!recapWindow.FocusDeath(death.DeathSeenAtTicks, death.MemberKeyHash))
            {
                Plugin.ChatGui.Print("[Better Deaths] That death recap is no longer available.");
            }

            ClosePopup();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{death.MemberName} ({death.ClassJobName})");
        }
    }

    private void ClosePopup()
    {
        SaveLastKnownPosition();
        pendingDeath = null;
        IsOpen = false;
    }

    private void ApplyPopupPosition()
    {
        if (!applySavedPositionOnNextDraw)
        {
            return;
        }

        applySavedPositionOnNextDraw = false;
        if (plugin.Configuration.HasDeathRecapPopupPosition)
        {
            ImGui.SetNextWindowPos(
                new Vector2(
                    plugin.Configuration.DeathRecapPopupPositionX,
                    plugin.Configuration.DeathRecapPopupPositionY),
                ImGuiCond.Always);
            return;
        }

        ref var viewportPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var viewportSize = ref ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowPos(
            viewportPosition + ((viewportSize - (DefaultSize * ImGuiHelpers.GlobalScale)) * 0.5f),
            ImGuiCond.Always);
    }

    private void SaveLastKnownPosition()
    {
        if (lastKnownPosition is { } position)
        {
            plugin.SaveDeathRecapPopupPosition(position);
        }
    }

    private void PushPopupStyle()
    {
        if (stylePushed)
        {
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WithAlpha(PopupWindowBgColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, WithAlpha(PopupTitleBgColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, WithAlpha(PopupTitleBgColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.12f)));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, WithAlpha(PopupTitleBgColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(PopupButtonColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(PopupButtonHoveredColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.12f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(PopupButtonActiveColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.20f)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        stylePushed = true;
    }

    private void PopPopupStyle()
    {
        if (!stylePushed)
        {
            return;
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(7);
        stylePushed = false;
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return color with { W = alpha };
    }

    private sealed record PendingDeath(
        DateTime SeenAtUtc,
        long DeathSeenAtTicks,
        uint MemberKeyHash,
        string MemberName,
        string ClassJobName);
}
