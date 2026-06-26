using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths;

internal sealed class BetterDeathsUiTheme
{
    public required BetterDeathsTheme Id { get; init; }

    public required string Label { get; init; }

    public required Vector4 DamageColor { get; init; }

    public required Vector4 HealColor { get; init; }

    public required Vector4 WarningColor { get; init; }

    public required Vector4 LeadUpGoldColor { get; init; }

    public required Vector4 SpamWarningColor { get; init; }

    public required Vector4 DisabledColor { get; init; }

    public required Vector4 UpdateBannerBgColor { get; init; }

    public required Vector4 UpdateBannerTextColor { get; init; }

    public required Vector4 NoticeBorderColor { get; init; }

    public required Vector4 NoticeTextColor { get; init; }

    public required Vector4 NoticeButtonColor { get; init; }

    public required Vector4 NoticeButtonHoveredColor { get; init; }

    public required Vector4 HpBarColor { get; init; }

    public required Vector4 ShieldBarColor { get; init; }

    public required Vector4 BarBackgroundColor { get; init; }

    public required Vector4 BarBorderColor { get; init; }

    public required Vector4 OverkillColor { get; init; }

    public required Vector4 ModernShellColor { get; init; }

    public required Vector4 ModernPanelColor { get; init; }

    public required Vector4 ModernPanelAltColor { get; init; }

    public required Vector4 ModernPanelBorderColor { get; init; }

    public required Vector4 ModernAccentColor { get; init; }

    public required Vector4 ModernAccentSoftColor { get; init; }

    public required Vector4 ModernMutedTextColor { get; init; }

    public required Vector4 ModernTextColor { get; init; }

    public required Vector4 ModernDividerColor { get; init; }

    public required Vector4 ModernFrameColor { get; init; }

    public required Vector4 ModernFrameHoveredColor { get; init; }

    public required Vector4 ModernButtonHoveredColor { get; init; }

    public required Vector4 ModernNavButtonColor { get; init; }

    public required Vector4 ModernNavButtonHoveredColor { get; init; }

    public required Vector4 ModernNavButtonSelectedColor { get; init; }

    public required Vector4 ModernNavButtonSelectedHoveredColor { get; init; }

    public required Vector4 ModernNavButtonActiveColor { get; init; }

    public required Vector4 ModernPopupBgColor { get; init; }

    public required Vector4 ModernCheckMarkColor { get; init; }

    public required Vector4 ModernSliderGrabColor { get; init; }

    public required Vector4 ModernSliderGrabActiveColor { get; init; }

    public required Vector4 ModernHeaderColor { get; init; }

    public required Vector4 ModernHeaderHoveredColor { get; init; }

    public required Vector4 ModernHeaderActiveColor { get; init; }

    public required float ModernFrameBorderSize { get; init; }

    public required Vector4 TimelineSelectedRowColor { get; init; }

    public required Vector4 TimelinePressedRowColor { get; init; }

    public required Vector4 ChangelogTabColor { get; init; }

    public required Vector4 ChangelogTabHoveredColor { get; init; }

    public required Vector4 ChangelogTabActiveColor { get; init; }

    public required Vector4 WidgetWindowBackgroundColor { get; init; }

    public required Vector4 WidgetTitleBackgroundColor { get; init; }

    public required Vector4 WidgetTitleActiveBackgroundColor { get; init; }

    public required Vector4 WidgetBorderColor { get; init; }

    public required Vector4 WidgetResizeGripColor { get; init; }

    public required Vector4 WidgetResizeGripHoveredColor { get; init; }

    public required Vector4 WidgetResizeGripActiveColor { get; init; }
}

internal static class BetterDeathsThemeCatalog
{
    private static readonly BetterDeathsUiTheme ClassicTheme = new()
    {
        Id = BetterDeathsTheme.Classic,
        Label = "Classic",
        DamageColor = new Vector4(1.0f, 0.35f, 0.25f, 1.0f),
        HealColor = new Vector4(0.25f, 1.0f, 0.45f, 1.0f),
        WarningColor = new Vector4(1.0f, 0.7f, 0.25f, 1.0f),
        LeadUpGoldColor = new Vector4(1.0f, 0.78f, 0.22f, 1.0f),
        SpamWarningColor = new Vector4(1.0f, 0.12f, 0.12f, 1.0f),
        DisabledColor = new Vector4(0.65f, 0.65f, 0.65f, 1.0f),
        UpdateBannerBgColor = new Vector4(0.16f, 0.24f, 0.12f, 0.95f),
        UpdateBannerTextColor = new Vector4(0.35f, 1.0f, 0.45f, 1.0f),
        NoticeBorderColor = new Vector4(0.37f, 0.92f, 0.83f, 1.0f),
        NoticeTextColor = new Vector4(0.84f, 1.0f, 0.97f, 1.0f),
        NoticeButtonColor = new Vector4(0.04f, 0.34f, 0.32f, 1.0f),
        NoticeButtonHoveredColor = new Vector4(0.06f, 0.46f, 0.43f, 1.0f),
        HpBarColor = new Vector4(0.2f, 0.75f, 0.35f, 1.0f),
        ShieldBarColor = new Vector4(1.0f, 0.82f, 0.16f, 1.0f),
        BarBackgroundColor = new Vector4(0.18f, 0.18f, 0.18f, 1.0f),
        BarBorderColor = new Vector4(0.45f, 0.45f, 0.45f, 1.0f),
        OverkillColor = new Vector4(1.0f, 0.05f, 0.05f, 1.0f),
        ModernShellColor = new Vector4(0.055f, 0.06f, 0.068f, 0.84f),
        ModernPanelColor = new Vector4(0.085f, 0.092f, 0.104f, 0.88f),
        ModernPanelAltColor = new Vector4(0.11f, 0.118f, 0.132f, 0.90f),
        ModernPanelBorderColor = new Vector4(0.22f, 0.25f, 0.28f, 0.95f),
        ModernAccentColor = new Vector4(0.36f, 0.92f, 0.82f, 1.0f),
        ModernAccentSoftColor = new Vector4(0.10f, 0.34f, 0.31f, 0.92f),
        ModernMutedTextColor = new Vector4(0.68f, 0.72f, 0.76f, 1.0f),
        ModernTextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
        ModernDividerColor = new Vector4(1.0f, 1.0f, 1.0f, 0.10f),
        ModernFrameColor = new Vector4(0.11f, 0.118f, 0.132f, 0.90f),
        ModernFrameHoveredColor = new Vector4(0.16f, 0.18f, 0.20f, 1.0f),
        ModernButtonHoveredColor = new Vector4(0.16f, 0.18f, 0.20f, 1.0f),
        ModernNavButtonColor = new Vector4(0.11f, 0.118f, 0.132f, 0.96f),
        ModernNavButtonHoveredColor = new Vector4(0.16f, 0.18f, 0.20f, 1.0f),
        ModernNavButtonSelectedColor = new Vector4(0.10f, 0.34f, 0.31f, 0.95f),
        ModernNavButtonSelectedHoveredColor = new Vector4(0.12f, 0.44f, 0.40f, 1.0f),
        ModernNavButtonActiveColor = new Vector4(0.12f, 0.44f, 0.40f, 1.0f),
        ModernPopupBgColor = new Vector4(0.085f, 0.092f, 0.104f, 0.98f),
        ModernCheckMarkColor = new Vector4(0.36f, 0.92f, 0.82f, 1.0f),
        ModernSliderGrabColor = new Vector4(0.36f, 0.92f, 0.82f, 0.72f),
        ModernSliderGrabActiveColor = new Vector4(0.36f, 0.92f, 0.82f, 1.0f),
        ModernHeaderColor = new Vector4(0.10f, 0.34f, 0.31f, 0.42f),
        ModernHeaderHoveredColor = new Vector4(0.16f, 0.18f, 0.20f, 1.0f),
        ModernHeaderActiveColor = new Vector4(0.10f, 0.34f, 0.31f, 0.92f),
        ModernFrameBorderSize = 0.0f,
        TimelineSelectedRowColor = new Vector4(0.28f, 0.22f, 0.10f, 0.55f),
        TimelinePressedRowColor = new Vector4(0.42f, 0.33f, 0.13f, 0.78f),
        ChangelogTabColor = new Vector4(0.34f, 0.24f, 0.07f, 0.95f),
        ChangelogTabHoveredColor = new Vector4(0.66f, 0.45f, 0.12f, 1.0f),
        ChangelogTabActiveColor = new Vector4(0.50f, 0.35f, 0.10f, 1.0f),
        WidgetWindowBackgroundColor = new Vector4(0.055f, 0.06f, 0.068f, 1.0f),
        WidgetTitleBackgroundColor = new Vector4(0.085f, 0.092f, 0.104f, 1.0f),
        WidgetTitleActiveBackgroundColor = new Vector4(0.10f, 0.11f, 0.125f, 1.0f),
        WidgetBorderColor = new Vector4(0.22f, 0.25f, 0.28f, 0.95f),
        WidgetResizeGripColor = new Vector4(0.36f, 0.92f, 0.82f, 0.30f),
        WidgetResizeGripHoveredColor = new Vector4(0.36f, 0.92f, 0.82f, 0.55f),
        WidgetResizeGripActiveColor = new Vector4(0.36f, 0.92f, 0.82f, 0.75f),
    };

    private static readonly BetterDeathsUiTheme RoseTheme = CreateTheme(
        BetterDeathsTheme.Rose,
        "Rose",
        shell: new Vector4(0.068f, 0.052f, 0.060f, 0.84f),
        panel: new Vector4(0.102f, 0.078f, 0.090f, 0.88f),
        panelAlt: new Vector4(0.135f, 0.098f, 0.116f, 0.90f),
        border: new Vector4(0.36f, 0.22f, 0.28f, 0.95f),
        accent: new Vector4(1.0f, 0.55f, 0.68f, 1.0f),
        accentSoft: new Vector4(0.42f, 0.13f, 0.22f, 0.92f),
        highlight: new Vector4(1.0f, 0.76f, 0.42f, 1.0f),
        muted: new Vector4(0.78f, 0.68f, 0.72f, 1.0f));

    private static readonly BetterDeathsUiTheme VerdantTheme = CreateTheme(
        BetterDeathsTheme.Verdant,
        "Verdant",
        shell: new Vector4(0.045f, 0.060f, 0.052f, 0.84f),
        panel: new Vector4(0.070f, 0.095f, 0.082f, 0.88f),
        panelAlt: new Vector4(0.095f, 0.125f, 0.105f, 0.90f),
        border: new Vector4(0.21f, 0.34f, 0.28f, 0.95f),
        accent: new Vector4(0.46f, 0.95f, 0.58f, 1.0f),
        accentSoft: new Vector4(0.11f, 0.36f, 0.21f, 0.92f),
        highlight: new Vector4(0.95f, 0.83f, 0.38f, 1.0f),
        muted: new Vector4(0.68f, 0.78f, 0.71f, 1.0f));

    private static readonly BetterDeathsUiTheme EmberTheme = CreateTheme(
        BetterDeathsTheme.Ember,
        "Ember",
        shell: new Vector4(0.070f, 0.058f, 0.048f, 0.84f),
        panel: new Vector4(0.105f, 0.085f, 0.068f, 0.88f),
        panelAlt: new Vector4(0.135f, 0.100f, 0.076f, 0.90f),
        border: new Vector4(0.36f, 0.27f, 0.20f, 0.95f),
        accent: new Vector4(1.0f, 0.52f, 0.28f, 1.0f),
        accentSoft: new Vector4(0.42f, 0.18f, 0.10f, 0.92f),
        highlight: new Vector4(1.0f, 0.79f, 0.36f, 1.0f),
        muted: new Vector4(0.78f, 0.70f, 0.62f, 1.0f));

    private static readonly BetterDeathsUiTheme PinkTheme = CreateTheme(
        BetterDeathsTheme.Pink,
        "Pink",
        shell: new Vector4(0.072f, 0.050f, 0.066f, 0.84f),
        panel: new Vector4(0.110f, 0.074f, 0.100f, 0.88f),
        panelAlt: new Vector4(0.150f, 0.094f, 0.132f, 0.90f),
        border: new Vector4(0.46f, 0.22f, 0.35f, 0.95f),
        accent: new Vector4(1.0f, 0.48f, 0.76f, 1.0f),
        accentSoft: new Vector4(0.46f, 0.12f, 0.30f, 0.92f),
        highlight: new Vector4(1.0f, 0.75f, 0.88f, 1.0f),
        muted: new Vector4(0.82f, 0.67f, 0.76f, 1.0f));

    private static readonly BetterDeathsUiTheme LavenderTheme = CreateTheme(
        BetterDeathsTheme.Lavender,
        "Lavender",
        shell: new Vector4(0.056f, 0.052f, 0.078f, 0.84f),
        panel: new Vector4(0.082f, 0.078f, 0.116f, 0.88f),
        panelAlt: new Vector4(0.110f, 0.100f, 0.154f, 0.90f),
        border: new Vector4(0.30f, 0.26f, 0.48f, 0.95f),
        accent: new Vector4(0.78f, 0.62f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.28f, 0.17f, 0.48f, 0.92f),
        highlight: new Vector4(0.92f, 0.79f, 1.0f, 1.0f),
        muted: new Vector4(0.72f, 0.68f, 0.82f, 1.0f));

    private static readonly BetterDeathsUiTheme BrightTheme = CreateTheme(
        BetterDeathsTheme.Bright,
        "Bright Mode",
        shell: new Vector4(0.91f, 0.93f, 0.94f, 0.92f),
        panel: new Vector4(0.96f, 0.97f, 0.97f, 0.96f),
        panelAlt: new Vector4(0.86f, 0.90f, 0.92f, 0.96f),
        border: new Vector4(0.54f, 0.62f, 0.66f, 0.95f),
        accent: new Vector4(0.00f, 0.46f, 0.58f, 1.0f),
        accentSoft: new Vector4(0.72f, 0.86f, 0.88f, 0.96f),
        highlight: new Vector4(0.86f, 0.52f, 0.00f, 1.0f),
        muted: new Vector4(0.33f, 0.39f, 0.43f, 1.0f),
        text: new Vector4(0.06f, 0.08f, 0.09f, 1.0f),
        danger: new Vector4(0.86f, 0.05f, 0.02f, 1.0f),
        frame: new Vector4(0.76f, 0.84f, 0.87f, 0.98f),
        frameHovered: new Vector4(0.68f, 0.81f, 0.84f, 1.0f),
        popup: new Vector4(0.94f, 0.97f, 0.97f, 0.99f),
        navButton: new Vector4(0.80f, 0.87f, 0.89f, 0.98f),
        navButtonHovered: new Vector4(0.70f, 0.82f, 0.85f, 1.0f),
        navButtonSelected: new Vector4(0.54f, 0.76f, 0.80f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.46f, 0.70f, 0.76f, 1.0f),
        frameBorderSize: 1.0f,
        barBackground: new Vector4(0.75f, 0.78f, 0.80f, 1.0f),
        barBorder: new Vector4(0.35f, 0.40f, 0.42f, 1.0f));

    private static readonly BetterDeathsUiTheme MarbleTheme = CreateTheme(
        BetterDeathsTheme.Marble,
        "Marble",
        shell: new Vector4(0.74f, 0.75f, 0.76f, 0.88f),
        panel: new Vector4(0.88f, 0.88f, 0.87f, 0.94f),
        panelAlt: new Vector4(0.79f, 0.80f, 0.80f, 0.94f),
        border: new Vector4(0.46f, 0.48f, 0.50f, 0.95f),
        accent: new Vector4(0.38f, 0.55f, 0.64f, 1.0f),
        accentSoft: new Vector4(0.64f, 0.72f, 0.76f, 0.94f),
        highlight: new Vector4(0.20f, 0.43f, 0.52f, 1.0f),
        muted: new Vector4(0.36f, 0.38f, 0.40f, 1.0f),
        text: new Vector4(0.08f, 0.09f, 0.10f, 1.0f),
        danger: new Vector4(0.80f, 0.06f, 0.04f, 1.0f),
        navButton: new Vector4(0.72f, 0.74f, 0.75f, 0.98f),
        navButtonHovered: new Vector4(0.64f, 0.68f, 0.70f, 1.0f),
        navButtonSelected: new Vector4(0.56f, 0.67f, 0.72f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.50f, 0.62f, 0.68f, 1.0f),
        barBackground: new Vector4(0.68f, 0.69f, 0.70f, 1.0f),
        barBorder: new Vector4(0.38f, 0.39f, 0.40f, 1.0f));

    private static readonly BetterDeathsUiTheme TabascoTheme = CreateTheme(
        BetterDeathsTheme.Tabasco,
        "Tabasco",
        shell: new Vector4(0.075f, 0.040f, 0.035f, 0.84f),
        panel: new Vector4(0.112f, 0.058f, 0.048f, 0.88f),
        panelAlt: new Vector4(0.152f, 0.070f, 0.056f, 0.90f),
        border: new Vector4(0.50f, 0.16f, 0.12f, 0.95f),
        accent: new Vector4(1.0f, 0.22f, 0.14f, 1.0f),
        accentSoft: new Vector4(0.48f, 0.08f, 0.05f, 0.92f),
        highlight: new Vector4(1.0f, 0.68f, 0.23f, 1.0f),
        muted: new Vector4(0.84f, 0.64f, 0.58f, 1.0f),
        danger: new Vector4(1.0f, 0.10f, 0.06f, 1.0f));

    private static readonly BetterDeathsUiTheme[] Themes =
    [
        ClassicTheme,
        RoseTheme,
        VerdantTheme,
        EmberTheme,
        PinkTheme,
        LavenderTheme,
        BrightTheme,
        MarbleTheme,
        TabascoTheme,
    ];

    public static IReadOnlyList<BetterDeathsUiTheme> All => Themes;

    public static BetterDeathsUiTheme GetTheme(BetterDeathsTheme theme)
    {
        foreach (var option in Themes)
        {
            if (option.Id == theme)
            {
                return option;
            }
        }

        return ClassicTheme;
    }

    private static BetterDeathsUiTheme CreateTheme(
        BetterDeathsTheme id,
        string label,
        Vector4 shell,
        Vector4 panel,
        Vector4 panelAlt,
        Vector4 border,
        Vector4 accent,
        Vector4 accentSoft,
        Vector4 highlight,
        Vector4 muted,
        Vector4? text = null,
        Vector4? danger = null,
        Vector4? frame = null,
        Vector4? frameHovered = null,
        Vector4? popup = null,
        Vector4? navButton = null,
        Vector4? navButtonHovered = null,
        Vector4? navButtonSelected = null,
        Vector4? navButtonSelectedHovered = null,
        Vector4? navButtonActive = null,
        float frameBorderSize = 0.0f,
        Vector4? barBackground = null,
        Vector4? barBorder = null)
    {
        var dangerColor = danger ?? new Vector4(1.0f, 0.25f, 0.20f, 1.0f);
        var textColor = text ?? new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        var frameColor = frame ?? panelAlt;
        var frameHoveredColor = frameHovered ?? panelAlt with { W = 1.0f };
        var popupBgColor = popup ?? panel with { W = 0.98f };
        var navButtonColor = navButton ?? panelAlt with { W = 0.96f };
        var navButtonHoveredColor = navButtonHovered ?? border with { W = 1.0f };
        var navButtonSelectedColor = navButtonSelected ?? accentSoft with { W = 0.95f };
        var navButtonSelectedHoveredColor = navButtonSelectedHovered ?? accentSoft with { W = 1.0f };
        return new BetterDeathsUiTheme
        {
            Id = id,
            Label = label,
            DamageColor = new Vector4(1.0f, 0.36f, 0.26f, 1.0f),
            HealColor = new Vector4(0.27f, 0.88f, 0.45f, 1.0f),
            WarningColor = new Vector4(1.0f, 0.68f, 0.28f, 1.0f),
            LeadUpGoldColor = highlight,
            SpamWarningColor = dangerColor,
            DisabledColor = new Vector4(0.64f, 0.64f, 0.64f, 1.0f),
            UpdateBannerBgColor = new Vector4(0.14f, 0.22f, 0.12f, 0.95f),
            UpdateBannerTextColor = new Vector4(0.45f, 1.0f, 0.52f, 1.0f),
            NoticeBorderColor = accent,
            NoticeTextColor = textColor,
            NoticeButtonColor = accentSoft,
            NoticeButtonHoveredColor = accentSoft with { X = MathF.Min(1.0f, accentSoft.X + 0.08f), Y = MathF.Min(1.0f, accentSoft.Y + 0.08f), Z = MathF.Min(1.0f, accentSoft.Z + 0.08f) },
            HpBarColor = new Vector4(0.24f, 0.74f, 0.36f, 1.0f),
            ShieldBarColor = highlight,
            BarBackgroundColor = barBackground ?? new Vector4(0.17f, 0.17f, 0.17f, 1.0f),
            BarBorderColor = barBorder ?? new Vector4(0.47f, 0.44f, 0.41f, 1.0f),
            OverkillColor = dangerColor,
            ModernShellColor = shell,
            ModernPanelColor = panel,
            ModernPanelAltColor = panelAlt,
            ModernPanelBorderColor = border,
            ModernAccentColor = accent,
            ModernAccentSoftColor = accentSoft,
            ModernMutedTextColor = muted,
            ModernTextColor = textColor,
            ModernDividerColor = new Vector4(1.0f, 1.0f, 1.0f, 0.10f),
            ModernFrameColor = frameColor,
            ModernFrameHoveredColor = frameHoveredColor,
            ModernButtonHoveredColor = border with { W = 1.0f },
            ModernNavButtonColor = navButtonColor,
            ModernNavButtonHoveredColor = navButtonHoveredColor,
            ModernNavButtonSelectedColor = navButtonSelectedColor,
            ModernNavButtonSelectedHoveredColor = navButtonSelectedHoveredColor,
            ModernNavButtonActiveColor = navButtonActive ?? navButtonSelectedHoveredColor,
            ModernPopupBgColor = popupBgColor,
            ModernCheckMarkColor = accent,
            ModernSliderGrabColor = accent with { W = 0.72f },
            ModernSliderGrabActiveColor = accent,
            ModernHeaderColor = accentSoft with { W = 0.42f },
            ModernHeaderHoveredColor = frameHoveredColor,
            ModernHeaderActiveColor = accentSoft with { W = 1.0f },
            ModernFrameBorderSize = frameBorderSize,
            TimelineSelectedRowColor = accentSoft with { W = 0.48f },
            TimelinePressedRowColor = accentSoft with { W = 0.72f },
            ChangelogTabColor = accentSoft with { W = 0.95f },
            ChangelogTabHoveredColor = border with { W = 1.0f },
            ChangelogTabActiveColor = accentSoft with { W = 1.0f },
            WidgetWindowBackgroundColor = shell with { W = 1.0f },
            WidgetTitleBackgroundColor = panel with { W = 1.0f },
            WidgetTitleActiveBackgroundColor = panelAlt with { W = 1.0f },
            WidgetBorderColor = border,
            WidgetResizeGripColor = accent with { W = 0.30f },
            WidgetResizeGripHoveredColor = accent with { W = 0.55f },
            WidgetResizeGripActiveColor = accent with { W = 0.75f },
        };
    }
}
