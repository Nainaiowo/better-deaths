using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class DeathRecapPopupWindow : Window, IDisposable
{
    private static readonly TimeSpan PopupLifetime = TimeSpan.FromSeconds(30);
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
            plugin.FormatPlayerDisplayName(death),
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

        var theme = BetterDeathsThemeCatalog.GetTheme(plugin.Configuration);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WithAlpha(theme.WidgetWindowBackgroundColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, WithAlpha(theme.WidgetTitleBackgroundColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, WithAlpha(theme.WidgetTitleActiveBackgroundColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.12f)));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, WithAlpha(theme.WidgetTitleBackgroundColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.Border, theme.WidgetBorderColor);
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(theme.ModernNavButtonSelectedColor, PopupBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(theme.ModernNavButtonSelectedHoveredColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.12f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(theme.ModernNavButtonActiveColor, MathF.Min(1.0f, PopupBackgroundOpacity + 0.20f)));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, theme.ModernPopupBgColor);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.ModernSelectedButtonTextColor);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.ModernMutedTextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        stylePushed = true;
    }

    private void PopPopupStyle()
    {
        if (!stylePushed)
        {
            return;
        }

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(11);
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
