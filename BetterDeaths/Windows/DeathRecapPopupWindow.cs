using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class DeathRecapPopupWindow : Window, IDisposable
{
    private static readonly TimeSpan PopupLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InternalCloseSuppressionWindow = TimeSpan.FromMilliseconds(500);
    private static readonly Vector2 DefaultSize = new(240.0f, 82.0f);
    private const float ButtonDragThreshold = 3.0f;
    private const float PopupBackgroundOpacity = 0.85f;
    private readonly Plugin plugin;
    private readonly RecapWindow recapWindow;
    private PendingDeath? pendingDeath;
    private DateTime? deathUpdatedAtUtc;
    private DateTime? testStartedAtUtc;
    private Vector2? lastKnownPosition;
    private DateTime? suppressUserCloseToggleUntilUtc;
    private bool applySavedPositionOnNextDraw;
    private bool buttonDragged;
    private bool stylePushed;

    public bool IsTestPopupActive => testStartedAtUtc is not null && IsOpen;

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

        Position = GetDefaultPopupPosition();
    }

    public void Dispose()
    {
        SaveLastKnownPosition();
    }

    public void DisplayDeath(PartyDeathRecord death)
    {
        testStartedAtUtc = null;
        deathUpdatedAtUtc = DateTime.UtcNow;
        buttonDragged = false;
        pendingDeath = new PendingDeath(
            death.SeenAtUtc,
            death.SeenAtUtc.Ticks,
            Plugin.GetMemberKeyHash(death.MemberKey),
            plugin.FormatPlayerDisplayName(death),
            death.ClassJobName);
        applySavedPositionOnNextDraw = true;
        IsOpen = true;
    }

    public bool DisplayTest()
    {
        if (pendingDeath is not null && IsOpen)
        {
            return false;
        }

        pendingDeath = null;
        deathUpdatedAtUtc = null;
        testStartedAtUtc = DateTime.UtcNow;
        buttonDragged = false;
        applySavedPositionOnNextDraw = true;
        IsOpen = true;
        return true;
    }

    public void RefreshVisibility()
    {
        if (!plugin.Configuration.ShowDeathRecapPopup)
        {
            if (IsOpen || pendingDeath is not null || testStartedAtUtc is not null)
            {
                ClosePopup();
            }

            return;
        }

        if (plugin.Configuration.KeepDeathRecapPopupVisible &&
            testStartedAtUtc is null)
        {
            if (!HasPersistentButtonTarget())
            {
                if (IsOpen)
                {
                    ClosePopup();
                }

                return;
            }

            if (ShouldShowPersistentDeathButton())
            {
                if (!IsOpen)
                {
                    applySavedPositionOnNextDraw = true;
                }

                IsOpen = true;
            }
            else
            {
                HidePopup();
            }
        }
    }

    public void CloseTest()
    {
        if (testStartedAtUtc is null)
        {
            return;
        }

        ClosePopup();
    }

    public override void OnClose()
    {
        SaveLastKnownPosition();
        if (ConsumeInternalCloseSuppression())
        {
            return;
        }

        var wasTestPopup = testStartedAtUtc is not null;
        pendingDeath = null;
        deathUpdatedAtUtc = null;
        testStartedAtUtc = null;
        buttonDragged = false;

        if (!wasTestPopup && plugin.Configuration.KeepDeathRecapPopupVisible)
        {
            plugin.SetKeepDeathRecapPopupVisible(false);
        }
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
        var currentPosition = ImGui.GetWindowPos();
        var clampedPosition = ClampPopupPosition(currentPosition, ImGui.GetWindowSize());
        if (clampedPosition != currentPosition)
        {
            ImGui.SetWindowPos(clampedPosition);
        }

        lastKnownPosition = clampedPosition;

        var now = DateTime.UtcNow;
        if (testStartedAtUtc is { } startedAtUtc)
        {
            if (now - startedAtUtc >= PopupLifetime)
            {
                ClosePopup();
                return;
            }

            _ = DrawDraggableButton("Test###BetterDeathsDeathRecapPopupButton");
            return;
        }

        if (!plugin.Configuration.ShowDeathRecapPopup)
        {
            ClosePopup();
            return;
        }

        var keepVisible = plugin.Configuration.KeepDeathRecapPopupVisible;
        if (pendingDeath is not { } death)
        {
            if (keepVisible)
            {
                DrawLatestPullButton();
            }
            else
            {
                ClosePopup();
            }

            return;
        }

        if (keepVisible && !ShouldShowPersistentDeathButton())
        {
            HidePopup();
            return;
        }

        if (!keepVisible &&
            (recapWindow.IsOpen || now - death.SeenAtUtc >= PopupLifetime))
        {
            ClosePopup();
            return;
        }

        var label = GetDeathButtonLabel(death, now, keepVisible);
        if (DrawDraggableButton(label))
        {
            if (keepVisible && ToggleMainWindowClosedIfOpen())
            {
                return;
            }

            if (!recapWindow.FocusDeath(death.DeathSeenAtTicks, death.MemberKeyHash))
            {
                Plugin.ChatGui.Print("[Better Deaths] That death recap is no longer available.");
                ClosePopup();
                return;
            }

            if (!keepVisible)
            {
                ClosePopup();
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{death.MemberName} ({death.ClassJobName})");
        }
    }

    private void DrawLatestPullButton()
    {
        if (!ShouldShowPersistentDeathButton())
        {
            HidePopup();
            return;
        }

        if (!recapWindow.HasLatestPull())
        {
            ClosePopup();
            return;
        }

        if (DrawDraggableButton("Open latest recap###BetterDeathsDeathRecapPopupButton"))
        {
            if (ToggleMainWindowClosedIfOpen())
            {
                return;
            }

            if (!recapWindow.FocusLatestPull())
            {
                Plugin.ChatGui.Print("[Better Deaths] No saved death recap is available yet.");
                ClosePopup();
                return;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Opens or closes Better Deaths on the newest saved pull.");
        }
    }

    private bool ToggleMainWindowClosedIfOpen()
    {
        if (!recapWindow.IsOpen)
        {
            return false;
        }

        recapWindow.IsOpen = false;
        return true;
    }

    private string GetDeathButtonLabel(PendingDeath death, DateTime now, bool keepVisible)
    {
        if (keepVisible)
        {
            var updated = deathUpdatedAtUtc is { } updatedAtUtc && now - updatedAtUtc < PopupLifetime;
            return $"{(updated ? "Updated!" : "Open death recap")}###BetterDeathsDeathRecapPopupButton";
        }

        var remainingSeconds = Math.Max(0, (int)Math.Ceiling((PopupLifetime - (now - death.SeenAtUtc)).TotalSeconds));
        return $"Open death recap ({remainingSeconds}s)###BetterDeathsDeathRecapPopupButton";
    }

    private bool ShouldShowPersistentDeathButton()
    {
        return plugin.Configuration.DeathRecapPopupVisibilityMode switch
        {
            DeathRecapPopupVisibilityMode.Always => true,
            _ => plugin.IsDutyStarted,
        };
    }

    private bool HasPersistentButtonTarget()
    {
        return pendingDeath is not null || recapWindow.HasLatestPull();
    }

    private bool DrawDraggableButton(string label)
    {
        var clicked = ImGui.Button(label, new Vector2(-1.0f, -1.0f));
        if (ImGui.IsItemActivated())
        {
            buttonDragged = false;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, ButtonDragThreshold))
        {
            var mouseDelta = ImGui.GetIO().MouseDelta;
            if (mouseDelta.LengthSquared() > 0.0f)
            {
                var position = ClampPopupPosition(ImGui.GetWindowPos() + mouseDelta, ImGui.GetWindowSize());
                ImGui.SetWindowPos(position);
                lastKnownPosition = position;
            }

            buttonDragged = true;
        }

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return clicked && !buttonDragged;
        }

        var shouldClick = clicked && !buttonDragged;
        buttonDragged = false;
        return shouldClick;
    }

    private void ClosePopup()
    {
        SaveLastKnownPosition();
        MarkInternalClose();
        pendingDeath = null;
        deathUpdatedAtUtc = null;
        testStartedAtUtc = null;
        buttonDragged = false;
        IsOpen = false;
    }

    private void HidePopup()
    {
        SaveLastKnownPosition();
        MarkInternalClose();
        testStartedAtUtc = null;
        buttonDragged = false;
        IsOpen = false;
    }

    private void MarkInternalClose()
    {
        suppressUserCloseToggleUntilUtc = DateTime.UtcNow + InternalCloseSuppressionWindow;
    }

    private bool ConsumeInternalCloseSuppression()
    {
        if (suppressUserCloseToggleUntilUtc is not { } suppressUntilUtc)
        {
            return false;
        }

        suppressUserCloseToggleUntilUtc = null;
        return DateTime.UtcNow <= suppressUntilUtc;
    }

    private void ApplyPopupPosition()
    {
        if (!applySavedPositionOnNextDraw)
        {
            return;
        }

        applySavedPositionOnNextDraw = false;
        var requestedPosition = plugin.Configuration.HasDeathRecapPopupPosition
            ? new Vector2(
                plugin.Configuration.DeathRecapPopupPositionX,
                plugin.Configuration.DeathRecapPopupPositionY)
            : GetDefaultPopupPosition();
        var position = ClampPopupPosition(requestedPosition, GetScaledDefaultSize());
        lastKnownPosition = position;
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
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

    private static Vector2 GetDefaultPopupPosition()
    {
        ref var viewportPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var viewportSize = ref ImGui.GetMainViewport().WorkSize;
        return viewportPosition + ((viewportSize - GetScaledDefaultSize()) * 0.5f);
    }

    private static Vector2 GetScaledDefaultSize()
    {
        return DefaultSize * ImGuiHelpers.GlobalScale;
    }

    private static Vector2 ClampPopupPosition(Vector2 position, Vector2 size)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            position = GetDefaultPopupPosition();
        }

        if (!float.IsFinite(size.X) || size.X <= 0.0f ||
            !float.IsFinite(size.Y) || size.Y <= 0.0f)
        {
            size = GetScaledDefaultSize();
        }

        ref var viewportPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var viewportSize = ref ImGui.GetMainViewport().WorkSize;
        var maxX = viewportPosition.X + MathF.Max(0.0f, viewportSize.X - size.X);
        var maxY = viewportPosition.Y + MathF.Max(0.0f, viewportSize.Y - size.Y);

        return new Vector2(
            Math.Clamp(position.X, viewportPosition.X, maxX),
            Math.Clamp(position.Y, viewportPosition.Y, maxY));
    }

    private sealed record PendingDeath(
        DateTime SeenAtUtc,
        long DeathSeenAtTicks,
        uint MemberKeyHash,
        string MemberName,
        string ClassJobName);
}
