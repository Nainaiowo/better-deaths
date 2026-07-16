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

    public required Vector4 ModernButtonTextColor { get; init; }

    public required Vector4 ModernSelectedButtonTextColor { get; init; }

    public required Vector4 ModernPopupBgColor { get; init; }

    public required Vector4 ModernCheckMarkColor { get; init; }

    public required Vector4 CheckboxFrameColor { get; init; }

    public required Vector4 CheckboxFrameHoveredColor { get; init; }

    public required Vector4 CheckboxFrameActiveColor { get; init; }

    public required Vector4 CheckboxBorderColor { get; init; }

    public required Vector4 ModernSliderGrabColor { get; init; }

    public required Vector4 ModernSliderGrabActiveColor { get; init; }

    public required Vector4 ModernHeaderColor { get; init; }

    public required Vector4 ModernHeaderHoveredColor { get; init; }

    public required Vector4 ModernHeaderActiveColor { get; init; }

    public required Vector4 TableRowAltColor { get; init; }

    public required Vector4 FocusedRowColor { get; init; }

    public required Vector4 FocusedRowAccentColor { get; init; }

    public required float ModernFrameBorderSize { get; init; }

    public required Vector4 TimelineSelectedRowColor { get; init; }

    public required Vector4 TimelinePressedRowColor { get; init; }

    public required Vector4 ScrollbarBackgroundColor { get; init; }

    public required Vector4 ScrollbarGrabColor { get; init; }

    public required Vector4 ScrollbarGrabHoveredColor { get; init; }

    public required Vector4 ScrollbarGrabActiveColor { get; init; }

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
    private static readonly Vector4 LightThemeDamageColor = new(0.74f, 0.04f, 0.04f, 1.0f);
    private static readonly Vector4 LightThemeHealColor = new(0.02f, 0.46f, 0.20f, 1.0f);

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
        ModernButtonTextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
        ModernSelectedButtonTextColor = new Vector4(0.98f, 0.98f, 0.96f, 1.0f),
        ModernPopupBgColor = new Vector4(0.085f, 0.092f, 0.104f, 0.98f),
        ModernCheckMarkColor = new Vector4(0.36f, 0.92f, 0.82f, 1.0f),
        CheckboxFrameColor = BlendColors(new Vector4(0.11f, 0.118f, 0.132f, 0.90f), new Vector4(0.22f, 0.25f, 0.28f, 0.95f), 0.42f) with { W = 0.96f },
        CheckboxFrameHoveredColor = BlendColors(new Vector4(0.16f, 0.18f, 0.20f, 1.0f), new Vector4(0.10f, 0.34f, 0.31f, 0.92f), 0.28f) with { W = 1.0f },
        CheckboxFrameActiveColor = new Vector4(0.10f, 0.34f, 0.31f, 1.0f),
        CheckboxBorderColor = BlendColors(new Vector4(0.22f, 0.25f, 0.28f, 0.95f), new Vector4(0.36f, 0.92f, 0.82f, 1.0f), 0.18f) with { W = 1.0f },
        ModernSliderGrabColor = new Vector4(0.36f, 0.92f, 0.82f, 0.72f),
        ModernSliderGrabActiveColor = new Vector4(0.36f, 0.92f, 0.82f, 1.0f),
        ModernHeaderColor = new Vector4(0.10f, 0.34f, 0.31f, 0.42f),
        ModernHeaderHoveredColor = new Vector4(0.16f, 0.18f, 0.20f, 1.0f),
        ModernHeaderActiveColor = new Vector4(0.10f, 0.34f, 0.31f, 0.92f),
        TableRowAltColor = BlendColors(new Vector4(0.085f, 0.092f, 0.104f, 0.88f), new Vector4(0.11f, 0.118f, 0.132f, 0.90f), 0.58f) with { W = 0.46f },
        FocusedRowColor = BlendColors(new Vector4(0.085f, 0.092f, 0.104f, 0.88f), new Vector4(0.11f, 0.118f, 0.132f, 0.90f), 0.58f) with { W = 0.46f },
        FocusedRowAccentColor = BlendColors(new Vector4(0.22f, 0.25f, 0.28f, 0.95f), new Vector4(0.36f, 0.92f, 0.82f, 1.0f), 0.32f) with { W = 0.74f },
        ModernFrameBorderSize = 0.0f,
        TimelineSelectedRowColor = new Vector4(0.28f, 0.22f, 0.10f, 0.55f),
        TimelinePressedRowColor = new Vector4(0.42f, 0.33f, 0.13f, 0.78f),
        ScrollbarBackgroundColor = BlendColors(new Vector4(0.055f, 0.06f, 0.068f, 0.84f), new Vector4(0.085f, 0.092f, 0.104f, 0.88f), 0.58f) with { W = 0.70f },
        ScrollbarGrabColor = BlendColors(new Vector4(0.22f, 0.25f, 0.28f, 0.95f), new Vector4(0.36f, 0.92f, 0.82f, 1.0f), 0.40f) with { W = 0.78f },
        ScrollbarGrabHoveredColor = BlendColors(BlendColors(new Vector4(0.22f, 0.25f, 0.28f, 0.95f), new Vector4(0.36f, 0.92f, 0.82f, 1.0f), 0.40f), new Vector4(0.36f, 0.92f, 0.82f, 1.0f), 0.34f) with { W = 0.94f },
        ScrollbarGrabActiveColor = BlendColors(new Vector4(0.36f, 0.92f, 0.82f, 1.0f), new Vector4(1.0f, 1.0f, 1.0f, 1.0f), 0.04f) with { W = 1.0f },
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
        "Bubblegum",
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
        "Potion",
        shell: new Vector4(0.056f, 0.052f, 0.078f, 0.84f),
        panel: new Vector4(0.082f, 0.078f, 0.116f, 0.88f),
        panelAlt: new Vector4(0.110f, 0.100f, 0.154f, 0.90f),
        border: new Vector4(0.30f, 0.26f, 0.48f, 0.95f),
        accent: new Vector4(0.78f, 0.62f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.28f, 0.17f, 0.48f, 0.92f),
        highlight: new Vector4(0.92f, 0.79f, 1.0f, 1.0f),
        muted: new Vector4(0.72f, 0.68f, 0.82f, 1.0f));

    private static readonly BetterDeathsUiTheme PastelBlueTheme = CreateTheme(
        BetterDeathsTheme.PastelBlue,
        "Moonlit",
        shell: new Vector4(0.046f, 0.058f, 0.086f, 0.84f),
        panel: new Vector4(0.066f, 0.086f, 0.128f, 0.88f),
        panelAlt: new Vector4(0.088f, 0.118f, 0.172f, 0.90f),
        border: new Vector4(0.22f, 0.34f, 0.50f, 0.95f),
        accent: new Vector4(0.62f, 0.82f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.14f, 0.28f, 0.48f, 0.92f),
        highlight: new Vector4(1.0f, 0.84f, 0.48f, 1.0f),
        muted: new Vector4(0.70f, 0.78f, 0.90f, 1.0f),
        frame: new Vector4(0.082f, 0.110f, 0.160f, 0.92f),
        frameHovered: new Vector4(0.13f, 0.20f, 0.30f, 1.0f),
        popup: new Vector4(0.060f, 0.080f, 0.118f, 0.99f),
        navButton: new Vector4(0.082f, 0.110f, 0.160f, 0.96f),
        navButtonHovered: new Vector4(0.13f, 0.20f, 0.30f, 1.0f),
        navButtonSelected: new Vector4(0.14f, 0.30f, 0.52f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.18f, 0.38f, 0.62f, 1.0f),
        divider: new Vector4(0.72f, 0.84f, 1.0f, 0.12f),
        barBackground: new Vector4(0.13f, 0.16f, 0.22f, 1.0f),
        barBorder: new Vector4(0.36f, 0.46f, 0.62f, 1.0f));

    private static readonly BetterDeathsUiTheme BrightTheme = CreateTheme(
        BetterDeathsTheme.Bright,
        "Daylight",
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
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.76f, 0.84f, 0.87f, 0.98f),
        frameHovered: new Vector4(0.68f, 0.81f, 0.84f, 1.0f),
        popup: new Vector4(0.94f, 0.97f, 0.97f, 0.99f),
        navButton: new Vector4(0.80f, 0.87f, 0.89f, 0.98f),
        navButtonHovered: new Vector4(0.70f, 0.82f, 0.85f, 1.0f),
        navButtonSelected: new Vector4(0.54f, 0.76f, 0.80f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.46f, 0.70f, 0.76f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.12f, 0.20f, 0.24f, 0.18f),
        barBackground: new Vector4(0.75f, 0.78f, 0.80f, 1.0f),
        barBorder: new Vector4(0.35f, 0.40f, 0.42f, 1.0f));

    private static readonly BetterDeathsUiTheme WisteriaTheme = CreateTheme(
        BetterDeathsTheme.Wisteria,
        "Wisteria",
        shell: new Vector4(0.91f, 0.88f, 0.96f, 0.92f),
        panel: new Vector4(0.97f, 0.94f, 1.0f, 0.96f),
        panelAlt: new Vector4(0.86f, 0.80f, 0.93f, 0.96f),
        border: new Vector4(0.58f, 0.48f, 0.72f, 0.95f),
        accent: new Vector4(0.42f, 0.20f, 0.62f, 1.0f),
        accentSoft: new Vector4(0.76f, 0.66f, 0.88f, 0.96f),
        highlight: new Vector4(0.12f, 0.02f, 0.22f, 1.0f),
        muted: new Vector4(0.12f, 0.09f, 0.18f, 1.0f),
        text: new Vector4(0.10f, 0.07f, 0.14f, 1.0f),
        danger: new Vector4(0.82f, 0.05f, 0.08f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.80f, 0.72f, 0.90f, 0.98f),
        frameHovered: new Vector4(0.72f, 0.62f, 0.84f, 1.0f),
        popup: new Vector4(0.97f, 0.94f, 1.0f, 0.99f),
        navButton: new Vector4(0.80f, 0.72f, 0.90f, 0.98f),
        navButtonHovered: new Vector4(0.72f, 0.62f, 0.84f, 1.0f),
        navButtonSelected: new Vector4(0.66f, 0.54f, 0.80f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.58f, 0.46f, 0.72f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.20f, 0.12f, 0.30f, 0.18f),
        barBackground: new Vector4(0.76f, 0.72f, 0.82f, 1.0f),
        barBorder: new Vector4(0.42f, 0.36f, 0.52f, 1.0f));

    private static readonly BetterDeathsUiTheme BlushTheme = CreateTheme(
        BetterDeathsTheme.Blush,
        "Blushies",
        shell: new Vector4(0.96f, 0.88f, 0.88f, 0.92f),
        panel: new Vector4(1.0f, 0.94f, 0.94f, 0.96f),
        panelAlt: new Vector4(0.94f, 0.80f, 0.80f, 0.96f),
        border: new Vector4(0.70f, 0.44f, 0.44f, 0.95f),
        accent: new Vector4(0.66f, 0.16f, 0.18f, 1.0f),
        accentSoft: new Vector4(0.88f, 0.62f, 0.62f, 0.96f),
        highlight: new Vector4(0.18f, 0.01f, 0.01f, 1.0f),
        muted: new Vector4(0.12f, 0.05f, 0.05f, 1.0f),
        text: new Vector4(0.14f, 0.06f, 0.06f, 1.0f),
        danger: new Vector4(0.82f, 0.03f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.90f, 0.72f, 0.72f, 0.98f),
        frameHovered: new Vector4(0.84f, 0.62f, 0.62f, 1.0f),
        popup: new Vector4(1.0f, 0.94f, 0.94f, 0.99f),
        navButton: new Vector4(0.90f, 0.72f, 0.72f, 0.98f),
        navButtonHovered: new Vector4(0.84f, 0.62f, 0.62f, 1.0f),
        navButtonSelected: new Vector4(0.78f, 0.52f, 0.52f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.70f, 0.42f, 0.42f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.30f, 0.10f, 0.10f, 0.18f),
        barBackground: new Vector4(0.84f, 0.72f, 0.72f, 1.0f),
        barBorder: new Vector4(0.48f, 0.34f, 0.34f, 1.0f));

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
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        navButton: new Vector4(0.72f, 0.74f, 0.75f, 0.98f),
        navButtonHovered: new Vector4(0.64f, 0.68f, 0.70f, 1.0f),
        navButtonSelected: new Vector4(0.56f, 0.67f, 0.72f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.50f, 0.62f, 0.68f, 1.0f),
        divider: new Vector4(0.16f, 0.18f, 0.20f, 0.18f),
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

    private static readonly BetterDeathsUiTheme MintTheme = CreateTheme(
        BetterDeathsTheme.Mint,
        "Mint",
        shell: new Vector4(0.82f, 0.92f, 0.88f, 0.92f),
        panel: new Vector4(0.92f, 0.98f, 0.95f, 0.96f),
        panelAlt: new Vector4(0.78f, 0.90f, 0.85f, 0.96f),
        border: new Vector4(0.38f, 0.60f, 0.52f, 0.95f),
        accent: new Vector4(0.00f, 0.48f, 0.36f, 1.0f),
        accentSoft: new Vector4(0.58f, 0.80f, 0.72f, 0.96f),
        highlight: new Vector4(0.82f, 0.50f, 0.00f, 1.0f),
        muted: new Vector4(0.25f, 0.40f, 0.36f, 1.0f),
        text: new Vector4(0.04f, 0.10f, 0.09f, 1.0f),
        danger: new Vector4(0.86f, 0.06f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.70f, 0.86f, 0.80f, 0.98f),
        frameHovered: new Vector4(0.62f, 0.80f, 0.74f, 1.0f),
        popup: new Vector4(0.91f, 0.97f, 0.94f, 0.99f),
        navButton: new Vector4(0.70f, 0.85f, 0.79f, 0.98f),
        navButtonHovered: new Vector4(0.60f, 0.78f, 0.72f, 1.0f),
        navButtonSelected: new Vector4(0.44f, 0.70f, 0.62f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.36f, 0.64f, 0.56f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.08f, 0.22f, 0.18f, 0.18f),
        barBackground: new Vector4(0.72f, 0.82f, 0.78f, 1.0f),
        barBorder: new Vector4(0.30f, 0.44f, 0.40f, 1.0f));

    private static readonly BetterDeathsUiTheme SkyTheme = CreateTheme(
        BetterDeathsTheme.Sky,
        "Sky",
        shell: new Vector4(0.82f, 0.90f, 0.96f, 0.92f),
        panel: new Vector4(0.92f, 0.97f, 1.0f, 0.96f),
        panelAlt: new Vector4(0.76f, 0.87f, 0.94f, 0.96f),
        border: new Vector4(0.36f, 0.54f, 0.68f, 0.95f),
        accent: new Vector4(0.00f, 0.36f, 0.70f, 1.0f),
        accentSoft: new Vector4(0.56f, 0.74f, 0.88f, 0.96f),
        highlight: new Vector4(0.86f, 0.48f, 0.02f, 1.0f),
        muted: new Vector4(0.25f, 0.36f, 0.46f, 1.0f),
        text: new Vector4(0.04f, 0.08f, 0.12f, 1.0f),
        danger: new Vector4(0.84f, 0.05f, 0.06f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.68f, 0.82f, 0.92f, 0.98f),
        frameHovered: new Vector4(0.58f, 0.76f, 0.88f, 1.0f),
        popup: new Vector4(0.91f, 0.96f, 1.0f, 0.99f),
        navButton: new Vector4(0.68f, 0.82f, 0.92f, 0.98f),
        navButtonHovered: new Vector4(0.58f, 0.76f, 0.88f, 1.0f),
        navButtonSelected: new Vector4(0.42f, 0.66f, 0.84f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.34f, 0.58f, 0.78f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.06f, 0.16f, 0.26f, 0.18f),
        barBackground: new Vector4(0.70f, 0.80f, 0.86f, 1.0f),
        barBorder: new Vector4(0.30f, 0.40f, 0.48f, 1.0f));

    private static readonly BetterDeathsUiTheme PeachTheme = CreateTheme(
        BetterDeathsTheme.Peach,
        "Peach",
        shell: new Vector4(0.94f, 0.86f, 0.82f, 0.92f),
        panel: new Vector4(1.0f, 0.94f, 0.90f, 0.96f),
        panelAlt: new Vector4(0.92f, 0.78f, 0.72f, 0.96f),
        border: new Vector4(0.66f, 0.42f, 0.34f, 0.95f),
        accent: new Vector4(0.74f, 0.22f, 0.14f, 1.0f),
        accentSoft: new Vector4(0.86f, 0.58f, 0.50f, 0.96f),
        highlight: new Vector4(0.72f, 0.38f, 0.00f, 1.0f),
        muted: new Vector4(0.43f, 0.30f, 0.26f, 1.0f),
        text: new Vector4(0.12f, 0.06f, 0.04f, 1.0f),
        danger: new Vector4(0.86f, 0.04f, 0.03f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.88f, 0.70f, 0.64f, 0.98f),
        frameHovered: new Vector4(0.82f, 0.62f, 0.56f, 1.0f),
        popup: new Vector4(1.0f, 0.93f, 0.89f, 0.99f),
        navButton: new Vector4(0.88f, 0.72f, 0.66f, 0.98f),
        navButtonHovered: new Vector4(0.82f, 0.62f, 0.56f, 1.0f),
        navButtonSelected: new Vector4(0.76f, 0.48f, 0.40f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.70f, 0.40f, 0.32f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.30f, 0.12f, 0.08f, 0.18f),
        barBackground: new Vector4(0.82f, 0.70f, 0.66f, 1.0f),
        barBorder: new Vector4(0.46f, 0.32f, 0.28f, 1.0f));

    private static readonly BetterDeathsUiTheme CloudTheme = CreateTheme(
        BetterDeathsTheme.Cloud,
        "Pillow",
        shell: new Vector4(0.88f, 0.91f, 0.94f, 0.92f),
        panel: new Vector4(0.97f, 0.98f, 0.99f, 0.96f),
        panelAlt: new Vector4(0.82f, 0.86f, 0.90f, 0.96f),
        border: new Vector4(0.48f, 0.55f, 0.62f, 0.95f),
        accent: new Vector4(0.28f, 0.40f, 0.58f, 1.0f),
        accentSoft: new Vector4(0.66f, 0.74f, 0.84f, 0.96f),
        highlight: new Vector4(0.76f, 0.46f, 0.04f, 1.0f),
        muted: new Vector4(0.34f, 0.38f, 0.44f, 1.0f),
        text: new Vector4(0.06f, 0.08f, 0.10f, 1.0f),
        danger: new Vector4(0.82f, 0.05f, 0.05f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.76f, 0.82f, 0.88f, 0.98f),
        frameHovered: new Vector4(0.68f, 0.76f, 0.84f, 1.0f),
        popup: new Vector4(0.96f, 0.98f, 1.0f, 0.99f),
        navButton: new Vector4(0.76f, 0.82f, 0.88f, 0.98f),
        navButtonHovered: new Vector4(0.68f, 0.76f, 0.84f, 1.0f),
        navButtonSelected: new Vector4(0.58f, 0.68f, 0.80f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.50f, 0.62f, 0.76f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.12f, 0.16f, 0.22f, 0.18f),
        barBackground: new Vector4(0.74f, 0.78f, 0.82f, 1.0f),
        barBorder: new Vector4(0.36f, 0.40f, 0.46f, 1.0f));

    private static readonly BetterDeathsUiTheme AbyssTheme = CreateTheme(
        BetterDeathsTheme.Abyss,
        "Abyss",
        shell: new Vector4(0.026f, 0.052f, 0.066f, 0.86f),
        panel: new Vector4(0.040f, 0.076f, 0.094f, 0.90f),
        panelAlt: new Vector4(0.056f, 0.104f, 0.124f, 0.92f),
        border: new Vector4(0.10f, 0.30f, 0.36f, 0.95f),
        accent: new Vector4(0.18f, 0.86f, 0.82f, 1.0f),
        accentSoft: new Vector4(0.06f, 0.32f, 0.34f, 0.92f),
        highlight: new Vector4(1.0f, 0.74f, 0.34f, 1.0f),
        muted: new Vector4(0.62f, 0.78f, 0.82f, 1.0f),
        frame: new Vector4(0.050f, 0.092f, 0.112f, 0.94f),
        frameHovered: new Vector4(0.070f, 0.140f, 0.166f, 1.0f),
        popup: new Vector4(0.034f, 0.064f, 0.080f, 0.99f),
        navButton: new Vector4(0.050f, 0.092f, 0.112f, 0.96f),
        navButtonHovered: new Vector4(0.070f, 0.140f, 0.166f, 1.0f),
        navButtonSelected: new Vector4(0.07f, 0.34f, 0.36f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.10f, 0.42f, 0.44f, 1.0f),
        divider: new Vector4(0.60f, 0.90f, 0.90f, 0.12f),
        barBackground: new Vector4(0.09f, 0.14f, 0.16f, 1.0f),
        barBorder: new Vector4(0.24f, 0.48f, 0.52f, 1.0f));

    private static readonly BetterDeathsUiTheme GraphiteTheme = CreateTheme(
        BetterDeathsTheme.Graphite,
        "Graphite",
        shell: new Vector4(0.035f, 0.038f, 0.042f, 0.86f),
        panel: new Vector4(0.058f, 0.062f, 0.070f, 0.90f),
        panelAlt: new Vector4(0.084f, 0.090f, 0.100f, 0.92f),
        border: new Vector4(0.24f, 0.26f, 0.30f, 0.95f),
        accent: new Vector4(0.64f, 0.82f, 0.96f, 1.0f),
        accentSoft: new Vector4(0.18f, 0.26f, 0.34f, 0.92f),
        highlight: new Vector4(0.96f, 0.76f, 0.34f, 1.0f),
        muted: new Vector4(0.68f, 0.70f, 0.74f, 1.0f),
        frame: new Vector4(0.078f, 0.084f, 0.094f, 0.94f),
        frameHovered: new Vector4(0.120f, 0.132f, 0.148f, 1.0f),
        popup: new Vector4(0.050f, 0.054f, 0.062f, 0.99f),
        navButton: new Vector4(0.078f, 0.084f, 0.094f, 0.96f),
        navButtonHovered: new Vector4(0.120f, 0.132f, 0.148f, 1.0f),
        navButtonSelected: new Vector4(0.22f, 0.30f, 0.38f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.28f, 0.38f, 0.48f, 1.0f),
        divider: new Vector4(0.82f, 0.86f, 0.90f, 0.11f),
        barBackground: new Vector4(0.13f, 0.13f, 0.14f, 1.0f),
        barBorder: new Vector4(0.38f, 0.40f, 0.44f, 1.0f));

    private static readonly BetterDeathsUiTheme GrapeTheme = CreateTheme(
        BetterDeathsTheme.Grape,
        "Grape",
        shell: new Vector4(0.054f, 0.034f, 0.078f, 0.86f),
        panel: new Vector4(0.082f, 0.050f, 0.116f, 0.90f),
        panelAlt: new Vector4(0.112f, 0.068f, 0.158f, 0.92f),
        border: new Vector4(0.28f, 0.18f, 0.46f, 0.95f),
        accent: new Vector4(0.78f, 0.48f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.32f, 0.14f, 0.52f, 0.92f),
        highlight: new Vector4(1.0f, 0.82f, 0.38f, 1.0f),
        muted: new Vector4(0.74f, 0.66f, 0.84f, 1.0f),
        frame: new Vector4(0.100f, 0.060f, 0.140f, 0.94f),
        frameHovered: new Vector4(0.150f, 0.084f, 0.218f, 1.0f),
        popup: new Vector4(0.066f, 0.040f, 0.094f, 0.99f),
        navButton: new Vector4(0.100f, 0.060f, 0.140f, 0.96f),
        navButtonHovered: new Vector4(0.150f, 0.084f, 0.218f, 1.0f),
        navButtonSelected: new Vector4(0.32f, 0.16f, 0.54f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.42f, 0.22f, 0.66f, 1.0f),
        divider: new Vector4(0.88f, 0.74f, 1.0f, 0.12f),
        barBackground: new Vector4(0.14f, 0.11f, 0.18f, 1.0f),
        barBorder: new Vector4(0.42f, 0.30f, 0.58f, 1.0f));

    private static readonly BetterDeathsUiTheme SodaTheme = CreateTheme(
        BetterDeathsTheme.Soda,
        "Soda",
        shell: new Vector4(0.040f, 0.050f, 0.066f, 0.86f),
        panel: new Vector4(0.060f, 0.074f, 0.098f, 0.90f),
        panelAlt: new Vector4(0.082f, 0.102f, 0.138f, 0.92f),
        border: new Vector4(0.18f, 0.28f, 0.42f, 0.95f),
        accent: new Vector4(0.42f, 0.94f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.10f, 0.30f, 0.48f, 0.92f),
        highlight: new Vector4(1.0f, 0.56f, 0.82f, 1.0f),
        muted: new Vector4(0.68f, 0.76f, 0.88f, 1.0f),
        frame: new Vector4(0.074f, 0.092f, 0.126f, 0.94f),
        frameHovered: new Vector4(0.110f, 0.154f, 0.218f, 1.0f),
        popup: new Vector4(0.052f, 0.064f, 0.086f, 0.99f),
        navButton: new Vector4(0.074f, 0.092f, 0.126f, 0.96f),
        navButtonHovered: new Vector4(0.110f, 0.154f, 0.218f, 1.0f),
        navButtonSelected: new Vector4(0.12f, 0.34f, 0.54f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.16f, 0.44f, 0.66f, 1.0f),
        divider: new Vector4(0.70f, 0.92f, 1.0f, 0.12f),
        barBackground: new Vector4(0.12f, 0.15f, 0.20f, 1.0f),
        barBorder: new Vector4(0.32f, 0.46f, 0.62f, 1.0f));

    private static readonly BetterDeathsUiTheme DrPepperTheme = CreateTheme(
        BetterDeathsTheme.DrPepper,
        "Dr Pepper",
        shell: new Vector4(0.050f, 0.018f, 0.026f, 0.86f),
        panel: new Vector4(0.078f, 0.026f, 0.038f, 0.90f),
        panelAlt: new Vector4(0.118f, 0.036f, 0.054f, 0.92f),
        border: new Vector4(0.44f, 0.14f, 0.18f, 0.95f),
        accent: new Vector4(1.0f, 0.30f, 0.34f, 1.0f),
        accentSoft: new Vector4(0.44f, 0.08f, 0.12f, 0.92f),
        highlight: new Vector4(1.0f, 0.78f, 0.54f, 1.0f),
        muted: new Vector4(0.86f, 0.62f, 0.66f, 1.0f),
        danger: new Vector4(1.0f, 0.18f, 0.18f, 1.0f),
        frame: new Vector4(0.102f, 0.032f, 0.046f, 0.94f),
        frameHovered: new Vector4(0.158f, 0.044f, 0.064f, 1.0f),
        popup: new Vector4(0.060f, 0.020f, 0.030f, 0.99f),
        navButton: new Vector4(0.102f, 0.032f, 0.046f, 0.96f),
        navButtonHovered: new Vector4(0.158f, 0.044f, 0.064f, 1.0f),
        navButtonSelected: new Vector4(0.42f, 0.09f, 0.13f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.54f, 0.12f, 0.17f, 1.0f),
        divider: new Vector4(1.0f, 0.66f, 0.68f, 0.12f),
        barBackground: new Vector4(0.15f, 0.08f, 0.08f, 1.0f),
        barBorder: new Vector4(0.48f, 0.18f, 0.20f, 1.0f));

    private static readonly BetterDeathsUiTheme SpriteTheme = CreateTheme(
        BetterDeathsTheme.Sprite,
        "Sprite",
        shell: new Vector4(0.86f, 0.94f, 0.89f, 0.92f),
        panel: new Vector4(0.96f, 1.0f, 0.96f, 0.96f),
        panelAlt: new Vector4(0.78f, 0.92f, 0.82f, 0.96f),
        border: new Vector4(0.32f, 0.62f, 0.42f, 0.95f),
        accent: new Vector4(0.00f, 0.54f, 0.22f, 1.0f),
        accentSoft: new Vector4(0.62f, 0.84f, 0.64f, 0.96f),
        highlight: new Vector4(0.62f, 0.42f, 0.00f, 1.0f),
        muted: new Vector4(0.22f, 0.42f, 0.30f, 1.0f),
        text: new Vector4(0.04f, 0.12f, 0.08f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.68f, 0.86f, 0.72f, 0.98f),
        frameHovered: new Vector4(0.58f, 0.80f, 0.64f, 1.0f),
        popup: new Vector4(0.96f, 1.0f, 0.96f, 0.99f),
        navButton: new Vector4(0.70f, 0.86f, 0.74f, 0.98f),
        navButtonHovered: new Vector4(0.58f, 0.80f, 0.64f, 1.0f),
        navButtonSelected: new Vector4(0.42f, 0.72f, 0.50f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.34f, 0.66f, 0.44f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.06f, 0.24f, 0.12f, 0.18f),
        barBackground: new Vector4(0.72f, 0.82f, 0.72f, 1.0f),
        barBorder: new Vector4(0.28f, 0.48f, 0.32f, 1.0f));

    private static readonly BetterDeathsUiTheme MountainDewTheme = CreateTheme(
        BetterDeathsTheme.MountainDew,
        "Mountain Dew",
        shell: new Vector4(0.84f, 0.92f, 0.58f, 0.92f),
        panel: new Vector4(0.94f, 1.0f, 0.70f, 0.96f),
        panelAlt: new Vector4(0.72f, 0.88f, 0.34f, 0.96f),
        border: new Vector4(0.34f, 0.56f, 0.14f, 0.95f),
        accent: new Vector4(0.04f, 0.46f, 0.14f, 1.0f),
        accentSoft: new Vector4(0.58f, 0.76f, 0.28f, 0.96f),
        highlight: new Vector4(0.66f, 0.24f, 0.00f, 1.0f),
        muted: new Vector4(0.24f, 0.36f, 0.12f, 1.0f),
        text: new Vector4(0.04f, 0.10f, 0.02f, 1.0f),
        danger: new Vector4(0.84f, 0.04f, 0.02f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.64f, 0.80f, 0.28f, 0.98f),
        frameHovered: new Vector4(0.56f, 0.74f, 0.20f, 1.0f),
        popup: new Vector4(0.94f, 1.0f, 0.70f, 0.99f),
        navButton: new Vector4(0.66f, 0.82f, 0.30f, 0.98f),
        navButtonHovered: new Vector4(0.56f, 0.74f, 0.20f, 1.0f),
        navButtonSelected: new Vector4(0.42f, 0.66f, 0.20f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.34f, 0.58f, 0.14f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.10f, 0.24f, 0.02f, 0.18f),
        barBackground: new Vector4(0.66f, 0.76f, 0.36f, 1.0f),
        barBorder: new Vector4(0.26f, 0.40f, 0.12f, 1.0f));

    private static readonly BetterDeathsUiTheme CokeTheme = CreateTheme(
        BetterDeathsTheme.Coke,
        "Coke",
        shell: new Vector4(0.050f, 0.030f, 0.030f, 0.86f),
        panel: new Vector4(0.078f, 0.044f, 0.044f, 0.90f),
        panelAlt: new Vector4(0.118f, 0.054f, 0.052f, 0.92f),
        border: new Vector4(0.46f, 0.16f, 0.14f, 0.95f),
        accent: new Vector4(1.0f, 0.20f, 0.16f, 1.0f),
        accentSoft: new Vector4(0.46f, 0.08f, 0.06f, 0.92f),
        highlight: new Vector4(0.94f, 0.92f, 0.86f, 1.0f),
        muted: new Vector4(0.84f, 0.68f, 0.66f, 1.0f),
        danger: new Vector4(1.0f, 0.12f, 0.10f, 1.0f),
        frame: new Vector4(0.100f, 0.050f, 0.048f, 0.94f),
        frameHovered: new Vector4(0.150f, 0.062f, 0.058f, 1.0f),
        popup: new Vector4(0.060f, 0.034f, 0.034f, 0.99f),
        navButton: new Vector4(0.100f, 0.050f, 0.048f, 0.96f),
        navButtonHovered: new Vector4(0.150f, 0.062f, 0.058f, 1.0f),
        navButtonSelected: new Vector4(0.42f, 0.08f, 0.06f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.54f, 0.12f, 0.08f, 1.0f),
        divider: new Vector4(1.0f, 0.92f, 0.86f, 0.12f),
        barBackground: new Vector4(0.15f, 0.10f, 0.10f, 1.0f),
        barBorder: new Vector4(0.48f, 0.20f, 0.18f, 1.0f));

    private static readonly BetterDeathsUiTheme FantaTheme = CreateTheme(
        BetterDeathsTheme.Fanta,
        "Fanta",
        shell: new Vector4(0.96f, 0.82f, 0.56f, 0.92f),
        panel: new Vector4(1.0f, 0.94f, 0.72f, 0.96f),
        panelAlt: new Vector4(0.94f, 0.68f, 0.34f, 0.96f),
        border: new Vector4(0.70f, 0.36f, 0.12f, 0.95f),
        accent: new Vector4(0.00f, 0.34f, 0.78f, 1.0f),
        accentSoft: new Vector4(0.92f, 0.56f, 0.18f, 0.96f),
        highlight: new Vector4(0.00f, 0.20f, 0.48f, 1.0f),
        muted: new Vector4(0.44f, 0.26f, 0.12f, 1.0f),
        text: new Vector4(0.12f, 0.06f, 0.02f, 1.0f),
        danger: new Vector4(0.84f, 0.04f, 0.02f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.86f, 0.58f, 0.26f, 0.98f),
        frameHovered: new Vector4(0.78f, 0.50f, 0.20f, 1.0f),
        popup: new Vector4(1.0f, 0.94f, 0.74f, 0.99f),
        navButton: new Vector4(0.88f, 0.60f, 0.28f, 0.98f),
        navButtonHovered: new Vector4(0.78f, 0.50f, 0.20f, 1.0f),
        navButtonSelected: new Vector4(0.44f, 0.58f, 0.86f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.34f, 0.50f, 0.78f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.28f, 0.12f, 0.02f, 0.18f),
        barBackground: new Vector4(0.82f, 0.62f, 0.36f, 1.0f),
        barBorder: new Vector4(0.46f, 0.28f, 0.12f, 1.0f));

    private static readonly BetterDeathsUiTheme GingerAleTheme = CreateTheme(
        BetterDeathsTheme.GingerAle,
        "Ginger Ale",
        shell: new Vector4(0.90f, 0.88f, 0.72f, 0.92f),
        panel: new Vector4(0.98f, 0.96f, 0.82f, 0.96f),
        panelAlt: new Vector4(0.82f, 0.78f, 0.56f, 0.96f),
        border: new Vector4(0.50f, 0.46f, 0.26f, 0.95f),
        accent: new Vector4(0.18f, 0.42f, 0.22f, 1.0f),
        accentSoft: new Vector4(0.68f, 0.72f, 0.46f, 0.96f),
        highlight: new Vector4(0.46f, 0.30f, 0.02f, 1.0f),
        muted: new Vector4(0.34f, 0.32f, 0.20f, 1.0f),
        text: new Vector4(0.10f, 0.09f, 0.04f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.03f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.74f, 0.70f, 0.48f, 0.98f),
        frameHovered: new Vector4(0.66f, 0.62f, 0.40f, 1.0f),
        popup: new Vector4(0.98f, 0.96f, 0.82f, 0.99f),
        navButton: new Vector4(0.76f, 0.72f, 0.50f, 0.98f),
        navButtonHovered: new Vector4(0.66f, 0.62f, 0.40f, 1.0f),
        navButtonSelected: new Vector4(0.56f, 0.62f, 0.36f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.48f, 0.56f, 0.30f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.18f, 0.16f, 0.06f, 0.18f),
        barBackground: new Vector4(0.74f, 0.70f, 0.54f, 1.0f),
        barBorder: new Vector4(0.38f, 0.34f, 0.20f, 1.0f));

    private static readonly BetterDeathsUiTheme PepsiTheme = CreateTheme(
        BetterDeathsTheme.Pepsi,
        "Pepsi",
        shell: new Vector4(0.028f, 0.040f, 0.078f, 0.86f),
        panel: new Vector4(0.042f, 0.058f, 0.112f, 0.90f),
        panelAlt: new Vector4(0.058f, 0.080f, 0.158f, 0.92f),
        border: new Vector4(0.14f, 0.26f, 0.54f, 0.95f),
        accent: new Vector4(0.32f, 0.66f, 1.0f, 1.0f),
        accentSoft: new Vector4(0.08f, 0.20f, 0.48f, 0.92f),
        highlight: new Vector4(1.0f, 0.44f, 0.40f, 1.0f),
        muted: new Vector4(0.64f, 0.72f, 0.88f, 1.0f),
        danger: new Vector4(1.0f, 0.20f, 0.20f, 1.0f),
        frame: new Vector4(0.052f, 0.074f, 0.142f, 0.94f),
        frameHovered: new Vector4(0.074f, 0.110f, 0.218f, 1.0f),
        popup: new Vector4(0.036f, 0.050f, 0.096f, 0.99f),
        navButton: new Vector4(0.052f, 0.074f, 0.142f, 0.96f),
        navButtonHovered: new Vector4(0.074f, 0.110f, 0.218f, 1.0f),
        navButtonSelected: new Vector4(0.08f, 0.22f, 0.56f, 0.96f),
        navButtonSelectedHovered: new Vector4(0.12f, 0.30f, 0.68f, 1.0f),
        divider: new Vector4(0.72f, 0.86f, 1.0f, 0.12f),
        barBackground: new Vector4(0.09f, 0.12f, 0.20f, 1.0f),
        barBorder: new Vector4(0.22f, 0.36f, 0.62f, 1.0f));

    private static readonly BetterDeathsUiTheme CallusTheme = CreateTheme(
        BetterDeathsTheme.Callus,
        "Callus",
        shell: new Vector4(0.86f, 0.82f, 0.92f, 0.92f),
        panel: new Vector4(0.96f, 0.93f, 1.0f, 0.96f),
        panelAlt: new Vector4(0.80f, 0.72f, 0.90f, 0.96f),
        border: new Vector4(0.56f, 0.46f, 0.66f, 0.95f),
        accent: new Vector4(0.70f, 0.48f, 0.06f, 1.0f),
        accentSoft: new Vector4(0.86f, 0.74f, 0.42f, 0.96f),
        highlight: new Vector4(0.46f, 0.25f, 0.02f, 1.0f),
        muted: new Vector4(0.28f, 0.20f, 0.38f, 1.0f),
        text: new Vector4(0.12f, 0.07f, 0.18f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.76f, 0.66f, 0.86f, 0.98f),
        frameHovered: new Vector4(0.68f, 0.56f, 0.80f, 1.0f),
        popup: new Vector4(0.96f, 0.93f, 1.0f, 0.99f),
        navButton: new Vector4(0.76f, 0.66f, 0.86f, 0.98f),
        navButtonHovered: new Vector4(0.68f, 0.56f, 0.80f, 1.0f),
        navButtonSelected: new Vector4(0.74f, 0.58f, 0.22f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.66f, 0.50f, 0.16f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.22f, 0.12f, 0.32f, 0.18f),
        barBackground: new Vector4(0.76f, 0.72f, 0.82f, 1.0f),
        barBorder: new Vector4(0.42f, 0.34f, 0.50f, 1.0f));

    private static readonly BetterDeathsUiTheme LemonadeTheme = CreateTheme(
        BetterDeathsTheme.Lemonade,
        "Lemonade",
        shell: new Vector4(0.94f, 0.92f, 0.78f, 0.92f),
        panel: new Vector4(1.0f, 0.98f, 0.86f, 0.96f),
        panelAlt: new Vector4(0.90f, 0.84f, 0.58f, 0.96f),
        border: new Vector4(0.64f, 0.58f, 0.28f, 0.95f),
        accent: new Vector4(0.60f, 0.46f, 0.00f, 1.0f),
        accentSoft: new Vector4(0.86f, 0.76f, 0.36f, 0.96f),
        highlight: new Vector4(0.46f, 0.34f, 0.00f, 1.0f),
        muted: new Vector4(0.38f, 0.34f, 0.18f, 1.0f),
        text: new Vector4(0.12f, 0.10f, 0.04f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.82f, 0.76f, 0.48f, 0.98f),
        frameHovered: new Vector4(0.76f, 0.68f, 0.38f, 1.0f),
        popup: new Vector4(1.0f, 0.98f, 0.86f, 0.99f),
        navButton: new Vector4(0.82f, 0.76f, 0.50f, 0.98f),
        navButtonHovered: new Vector4(0.76f, 0.68f, 0.38f, 1.0f),
        navButtonSelected: new Vector4(0.72f, 0.62f, 0.20f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.64f, 0.54f, 0.12f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.28f, 0.24f, 0.08f, 0.18f),
        barBackground: new Vector4(0.80f, 0.76f, 0.58f, 1.0f),
        barBorder: new Vector4(0.42f, 0.38f, 0.20f, 1.0f));

    private static readonly BetterDeathsUiTheme CottonTheme = CreateTheme(
        BetterDeathsTheme.Cotton,
        "Cotton",
        shell: new Vector4(0.88f, 0.92f, 0.98f, 0.92f),
        panel: new Vector4(0.97f, 0.99f, 1.0f, 0.96f),
        panelAlt: new Vector4(0.78f, 0.86f, 0.96f, 0.96f),
        border: new Vector4(0.48f, 0.58f, 0.72f, 0.95f),
        accent: new Vector4(0.64f, 0.24f, 0.54f, 1.0f),
        accentSoft: new Vector4(0.84f, 0.66f, 0.86f, 0.96f),
        highlight: new Vector4(0.44f, 0.18f, 0.36f, 1.0f),
        muted: new Vector4(0.30f, 0.36f, 0.46f, 1.0f),
        text: new Vector4(0.06f, 0.08f, 0.14f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.70f, 0.80f, 0.92f, 0.98f),
        frameHovered: new Vector4(0.62f, 0.74f, 0.88f, 1.0f),
        popup: new Vector4(0.96f, 0.99f, 1.0f, 0.99f),
        navButton: new Vector4(0.70f, 0.80f, 0.92f, 0.98f),
        navButtonHovered: new Vector4(0.62f, 0.74f, 0.88f, 1.0f),
        navButtonSelected: new Vector4(0.68f, 0.54f, 0.78f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.58f, 0.44f, 0.70f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.08f, 0.14f, 0.24f, 0.18f),
        barBackground: new Vector4(0.72f, 0.78f, 0.86f, 1.0f),
        barBorder: new Vector4(0.34f, 0.40f, 0.52f, 1.0f));

    private static readonly BetterDeathsUiTheme BananaTheme = CreateTheme(
        BetterDeathsTheme.Banana,
        "Minion",
        shell: new Vector4(0.94f, 0.86f, 0.32f, 0.92f),
        panel: new Vector4(1.0f, 0.95f, 0.50f, 0.96f),
        panelAlt: new Vector4(0.94f, 0.80f, 0.18f, 0.96f),
        border: new Vector4(0.16f, 0.26f, 0.52f, 0.95f),
        accent: new Vector4(0.07f, 0.24f, 0.58f, 1.0f),
        accentSoft: new Vector4(0.38f, 0.56f, 0.90f, 0.96f),
        highlight: new Vector4(0.05f, 0.18f, 0.46f, 1.0f),
        muted: new Vector4(0.20f, 0.24f, 0.34f, 1.0f),
        text: new Vector4(0.04f, 0.06f, 0.12f, 1.0f),
        danger: new Vector4(0.84f, 0.03f, 0.03f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.82f, 0.68f, 0.18f, 0.98f),
        frameHovered: new Vector4(0.74f, 0.60f, 0.12f, 1.0f),
        popup: new Vector4(1.0f, 0.96f, 0.58f, 0.99f),
        navButton: new Vector4(0.84f, 0.70f, 0.20f, 0.98f),
        navButtonHovered: new Vector4(0.74f, 0.60f, 0.14f, 1.0f),
        navButtonSelected: new Vector4(0.18f, 0.34f, 0.70f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.10f, 0.26f, 0.58f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.06f, 0.16f, 0.36f, 0.20f),
        barBackground: new Vector4(0.78f, 0.68f, 0.26f, 1.0f),
        barBorder: new Vector4(0.14f, 0.26f, 0.52f, 1.0f));

    private static readonly BetterDeathsUiTheme HamtaroTheme = CreateTheme(
        BetterDeathsTheme.Hamtaro,
        "Hamtaro",
        shell: new Vector4(0.96f, 0.88f, 0.76f, 0.92f),
        panel: new Vector4(1.0f, 0.96f, 0.86f, 0.96f),
        panelAlt: new Vector4(0.92f, 0.76f, 0.56f, 0.96f),
        border: new Vector4(0.58f, 0.34f, 0.18f, 0.95f),
        accent: new Vector4(0.78f, 0.32f, 0.08f, 1.0f),
        accentSoft: new Vector4(0.92f, 0.58f, 0.34f, 0.96f),
        highlight: new Vector4(0.48f, 0.20f, 0.08f, 1.0f),
        muted: new Vector4(0.40f, 0.28f, 0.22f, 1.0f),
        text: new Vector4(0.12f, 0.06f, 0.03f, 1.0f),
        danger: new Vector4(0.82f, 0.04f, 0.04f, 1.0f),
        damage: LightThemeDamageColor,
        heal: LightThemeHealColor,
        frame: new Vector4(0.82f, 0.64f, 0.46f, 0.98f),
        frameHovered: new Vector4(0.74f, 0.52f, 0.34f, 1.0f),
        popup: new Vector4(1.0f, 0.96f, 0.86f, 0.99f),
        navButton: new Vector4(0.84f, 0.66f, 0.48f, 0.98f),
        navButtonHovered: new Vector4(0.74f, 0.52f, 0.34f, 1.0f),
        navButtonSelected: new Vector4(0.78f, 0.42f, 0.18f, 0.98f),
        navButtonSelectedHovered: new Vector4(0.72f, 0.38f, 0.16f, 1.0f),
        frameBorderSize: 1.0f,
        divider: new Vector4(0.28f, 0.12f, 0.06f, 0.18f),
        barBackground: new Vector4(0.80f, 0.66f, 0.52f, 1.0f),
        barBorder: new Vector4(0.42f, 0.24f, 0.14f, 1.0f));

    private static readonly BetterDeathsUiTheme[] Themes =
    [
        ClassicTheme,
        GraphiteTheme,
        AbyssTheme,
        PastelBlueTheme,
        SodaTheme,
        DrPepperTheme,
        CokeTheme,
        PepsiTheme,
        VerdantTheme,
        SpriteTheme,
        MountainDewTheme,
        RoseTheme,
        PinkTheme,
        LavenderTheme,
        GrapeTheme,
        EmberTheme,
        TabascoTheme,
        BrightTheme,
        MarbleTheme,
        CloudTheme,
        CottonTheme,
        FantaTheme,
        GingerAleTheme,
        SkyTheme,
        MintTheme,
        WisteriaTheme,
        CallusTheme,
        BlushTheme,
        PeachTheme,
        BananaTheme,
        HamtaroTheme,
        LemonadeTheme,
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

    public static BetterDeathsUiTheme GetTheme(Configuration configuration)
    {
        var baseTheme = GetTheme(configuration.Theme);
        return configuration.CustomTheme is { Enabled: true, Initialized: true } customTheme
            ? ApplyCustomTheme(baseTheme, customTheme)
            : baseTheme;
    }

    public static CustomThemeConfiguration CreateCustomThemeConfiguration(BetterDeathsUiTheme theme)
    {
        return new CustomThemeConfiguration
        {
            SchemaVersion = CustomThemeConfiguration.CurrentSchemaVersion,
            Enabled = true,
            Initialized = true,
            WindowBackground = ToThemeColorValue(theme.ModernShellColor),
            ContentBackground = ToThemeColorValue(theme.ModernPanelColor),
            RaisedBackground = ToThemeColorValue(theme.ModernPanelAltColor),
            Border = ToThemeColorValue(theme.ModernPanelBorderColor),
            Divider = ToThemeColorValue(theme.ModernDividerColor),
            Accent = ToThemeColorValue(theme.ModernAccentColor),
            AccentSoft = ToThemeColorValue(theme.ModernAccentSoftColor),
            RegularText = ToThemeColorValue(theme.ModernTextColor),
            MutedText = ToThemeColorValue(theme.ModernMutedTextColor),
            GoldText = ToThemeColorValue(theme.LeadUpGoldColor),
            DisabledText = ToThemeColorValue(theme.DisabledColor),
            DamageText = ToThemeColorValue(theme.DamageColor),
            HealText = ToThemeColorValue(theme.HealColor),
            WarningText = ToThemeColorValue(theme.WarningColor),
            SpamWarningText = ToThemeColorValue(theme.SpamWarningColor),
            OverkillText = ToThemeColorValue(theme.OverkillColor),
            FrameBackground = ToThemeColorValue(theme.ModernFrameColor),
            FrameHoverBackground = ToThemeColorValue(theme.ModernFrameHoveredColor),
            PopupBackground = ToThemeColorValue(theme.ModernPopupBgColor),
            ButtonColor = ToThemeColorValue(theme.ModernNavButtonColor),
            ButtonHoverColor = ToThemeColorValue(theme.ModernNavButtonHoveredColor),
            SelectedButtonColor = ToThemeColorValue(theme.ModernNavButtonSelectedColor),
            SelectedButtonHoverColor = ToThemeColorValue(theme.ModernNavButtonSelectedHoveredColor),
            ButtonActiveColor = ToThemeColorValue(theme.ModernNavButtonActiveColor),
            ButtonText = ToThemeColorValue(theme.ModernButtonTextColor),
            SelectedButtonText = ToThemeColorValue(theme.ModernSelectedButtonTextColor),
            CheckboxBackground = ToThemeColorValue(theme.CheckboxFrameColor),
            CheckboxHoverBackground = ToThemeColorValue(theme.CheckboxFrameHoveredColor),
            CheckboxActiveBackground = ToThemeColorValue(theme.CheckboxFrameActiveColor),
            CheckboxCheckMark = ToThemeColorValue(theme.ModernCheckMarkColor),
            CheckboxBorder = ToThemeColorValue(theme.CheckboxBorderColor),
            SliderGrab = ToThemeColorValue(theme.ModernSliderGrabColor),
            SliderGrabActive = ToThemeColorValue(theme.ModernSliderGrabActiveColor),
            HeaderBackground = ToThemeColorValue(theme.ModernHeaderColor),
            HeaderHoverBackground = ToThemeColorValue(theme.ModernHeaderHoveredColor),
            HeaderActiveBackground = ToThemeColorValue(theme.ModernHeaderActiveColor),
            TableRowAlt = ToThemeColorValue(theme.TableRowAltColor),
            FocusedRow = ToThemeColorValue(theme.FocusedRowColor),
            FocusedRowAccent = ToThemeColorValue(theme.FocusedRowAccentColor),
            TimelineSelectedRow = ToThemeColorValue(theme.TimelineSelectedRowColor),
            TimelinePressedRow = ToThemeColorValue(theme.TimelinePressedRowColor),
            ScrollbarBackground = ToThemeColorValue(theme.ScrollbarBackgroundColor),
            ScrollbarGrab = ToThemeColorValue(theme.ScrollbarGrabColor),
            ScrollbarGrabHover = ToThemeColorValue(theme.ScrollbarGrabHoveredColor),
            ScrollbarGrabActive = ToThemeColorValue(theme.ScrollbarGrabActiveColor),
            ChangelogTab = ToThemeColorValue(theme.ChangelogTabColor),
            ChangelogTabHover = ToThemeColorValue(theme.ChangelogTabHoveredColor),
            ChangelogTabActive = ToThemeColorValue(theme.ChangelogTabActiveColor),
            HpBar = ToThemeColorValue(theme.HpBarColor),
            ShieldBar = ToThemeColorValue(theme.ShieldBarColor),
            BarBackground = ToThemeColorValue(theme.BarBackgroundColor),
            BarBorder = ToThemeColorValue(theme.BarBorderColor),
            WidgetWindowBackground = ToThemeColorValue(theme.WidgetWindowBackgroundColor),
            WidgetTitleBackground = ToThemeColorValue(theme.WidgetTitleBackgroundColor),
            WidgetTitleActiveBackground = ToThemeColorValue(theme.WidgetTitleActiveBackgroundColor),
            WidgetBorder = ToThemeColorValue(theme.WidgetBorderColor),
            WidgetResizeGrip = ToThemeColorValue(theme.WidgetResizeGripColor),
            WidgetResizeGripHover = ToThemeColorValue(theme.WidgetResizeGripHoveredColor),
            WidgetResizeGripActive = ToThemeColorValue(theme.WidgetResizeGripActiveColor),
            UpdateBannerBackground = ToThemeColorValue(theme.UpdateBannerBgColor),
            UpdateBannerText = ToThemeColorValue(theme.UpdateBannerTextColor),
            NoticeBorder = ToThemeColorValue(theme.NoticeBorderColor),
            NoticeText = ToThemeColorValue(theme.NoticeTextColor),
            NoticeButton = ToThemeColorValue(theme.NoticeButtonColor),
            NoticeButtonHover = ToThemeColorValue(theme.NoticeButtonHoveredColor),
        };
    }

    private static BetterDeathsUiTheme ApplyCustomTheme(BetterDeathsUiTheme baseTheme, CustomThemeConfiguration customTheme)
    {
        var shell = ToVector4(customTheme.WindowBackground, baseTheme.ModernShellColor);
        var panel = ToVector4(customTheme.ContentBackground, baseTheme.ModernPanelColor);
        var panelAlt = ToVector4(customTheme.RaisedBackground, baseTheme.ModernPanelAltColor);
        var border = ToVector4(customTheme.Border, baseTheme.ModernPanelBorderColor);
        var divider = ToVector4(customTheme.Divider, baseTheme.ModernDividerColor);
        var accent = ToVector4(customTheme.Accent, baseTheme.ModernAccentColor);
        var accentSoft = ToVector4(customTheme.AccentSoft, baseTheme.ModernAccentSoftColor);
        var text = ToVector4(customTheme.RegularText, baseTheme.ModernTextColor);
        var muted = ToVector4(customTheme.MutedText, baseTheme.ModernMutedTextColor);
        var gold = ToVector4(customTheme.GoldText, baseTheme.LeadUpGoldColor);
        var disabled = ToVector4(customTheme.DisabledText, baseTheme.DisabledColor);
        var damage = ToVector4(customTheme.DamageText, baseTheme.DamageColor);
        var heal = ToVector4(customTheme.HealText, baseTheme.HealColor);
        var warning = ToVector4(customTheme.WarningText, baseTheme.WarningColor);
        var spamWarning = ToVector4(customTheme.SpamWarningText, baseTheme.SpamWarningColor);
        var overkill = ToVector4(customTheme.OverkillText, baseTheme.OverkillColor);
        var frame = ToVector4(customTheme.FrameBackground, baseTheme.ModernFrameColor);
        var frameHovered = ToVector4(customTheme.FrameHoverBackground, baseTheme.ModernFrameHoveredColor);
        var popup = ToVector4(customTheme.PopupBackground, baseTheme.ModernPopupBgColor);
        var button = ToVector4(customTheme.ButtonColor, baseTheme.ModernNavButtonColor);
        var buttonHover = ToVector4(customTheme.ButtonHoverColor, baseTheme.ModernNavButtonHoveredColor);
        var selectedButton = ToVector4(customTheme.SelectedButtonColor, baseTheme.ModernNavButtonSelectedColor);
        var selectedButtonHover = ToVector4(customTheme.SelectedButtonHoverColor, baseTheme.ModernNavButtonSelectedHoveredColor);
        var buttonActive = ToVector4(customTheme.ButtonActiveColor, baseTheme.ModernNavButtonActiveColor);
        var buttonText = ToVector4(customTheme.ButtonText, baseTheme.ModernButtonTextColor);
        var selectedButtonText = ToVector4(customTheme.SelectedButtonText, baseTheme.ModernSelectedButtonTextColor);
        var checkboxFrame = ToVector4(customTheme.CheckboxBackground, baseTheme.CheckboxFrameColor);
        var checkboxFrameHovered = ToVector4(customTheme.CheckboxHoverBackground, baseTheme.CheckboxFrameHoveredColor);
        var checkboxFrameActive = ToVector4(customTheme.CheckboxActiveBackground, baseTheme.CheckboxFrameActiveColor);
        var checkboxCheckMark = ToVector4(customTheme.CheckboxCheckMark, baseTheme.ModernCheckMarkColor);
        var checkboxBorder = ToVector4(customTheme.CheckboxBorder, baseTheme.CheckboxBorderColor);
        var sliderGrab = ToVector4(customTheme.SliderGrab, baseTheme.ModernSliderGrabColor);
        var sliderGrabActive = ToVector4(customTheme.SliderGrabActive, baseTheme.ModernSliderGrabActiveColor);
        var header = ToVector4(customTheme.HeaderBackground, baseTheme.ModernHeaderColor);
        var headerHover = ToVector4(customTheme.HeaderHoverBackground, baseTheme.ModernHeaderHoveredColor);
        var headerActive = ToVector4(customTheme.HeaderActiveBackground, baseTheme.ModernHeaderActiveColor);
        var tableRowAlt = ToVector4(customTheme.TableRowAlt, baseTheme.TableRowAltColor);
        var focusedRow = ToVector4(customTheme.FocusedRow, baseTheme.FocusedRowColor);
        var focusedRowAccent = ToVector4(customTheme.FocusedRowAccent, baseTheme.FocusedRowAccentColor);
        var timelineSelected = ToVector4(customTheme.TimelineSelectedRow, baseTheme.TimelineSelectedRowColor);
        var timelinePressed = ToVector4(customTheme.TimelinePressedRow, baseTheme.TimelinePressedRowColor);
        var scrollbarBackground = ToVector4(customTheme.ScrollbarBackground, baseTheme.ScrollbarBackgroundColor);
        var scrollbarGrab = ToVector4(customTheme.ScrollbarGrab, baseTheme.ScrollbarGrabColor);
        var scrollbarGrabHover = ToVector4(customTheme.ScrollbarGrabHover, baseTheme.ScrollbarGrabHoveredColor);
        var scrollbarGrabActive = ToVector4(customTheme.ScrollbarGrabActive, baseTheme.ScrollbarGrabActiveColor);
        var changelogTab = ToVector4(customTheme.ChangelogTab, baseTheme.ChangelogTabColor);
        var changelogTabHover = ToVector4(customTheme.ChangelogTabHover, baseTheme.ChangelogTabHoveredColor);
        var changelogTabActive = ToVector4(customTheme.ChangelogTabActive, baseTheme.ChangelogTabActiveColor);
        var hpBar = ToVector4(customTheme.HpBar, baseTheme.HpBarColor);
        var shieldBar = ToVector4(customTheme.ShieldBar, baseTheme.ShieldBarColor);
        var barBackground = ToVector4(customTheme.BarBackground, baseTheme.BarBackgroundColor);
        var barBorder = ToVector4(customTheme.BarBorder, baseTheme.BarBorderColor);
        var widgetWindow = ToVector4(customTheme.WidgetWindowBackground, baseTheme.WidgetWindowBackgroundColor);
        var widgetTitle = ToVector4(customTheme.WidgetTitleBackground, baseTheme.WidgetTitleBackgroundColor);
        var widgetTitleActive = ToVector4(customTheme.WidgetTitleActiveBackground, baseTheme.WidgetTitleActiveBackgroundColor);
        var widgetBorder = ToVector4(customTheme.WidgetBorder, baseTheme.WidgetBorderColor);
        var widgetResizeGrip = ToVector4(customTheme.WidgetResizeGrip, baseTheme.WidgetResizeGripColor);
        var widgetResizeGripHover = ToVector4(customTheme.WidgetResizeGripHover, baseTheme.WidgetResizeGripHoveredColor);
        var widgetResizeGripActive = ToVector4(customTheme.WidgetResizeGripActive, baseTheme.WidgetResizeGripActiveColor);
        var updateBannerBg = ToVector4(customTheme.UpdateBannerBackground, baseTheme.UpdateBannerBgColor);
        var updateBannerText = ToVector4(customTheme.UpdateBannerText, baseTheme.UpdateBannerTextColor);
        var noticeBorder = ToVector4(customTheme.NoticeBorder, baseTheme.NoticeBorderColor);
        var noticeText = ToVector4(customTheme.NoticeText, baseTheme.NoticeTextColor);
        var noticeButton = ToVector4(customTheme.NoticeButton, baseTheme.NoticeButtonColor);
        var noticeButtonHover = ToVector4(customTheme.NoticeButtonHover, baseTheme.NoticeButtonHoveredColor);

        return new BetterDeathsUiTheme
        {
            Id = baseTheme.Id,
            Label = $"{baseTheme.Label} Custom",
            DamageColor = damage,
            HealColor = heal,
            WarningColor = warning,
            LeadUpGoldColor = gold,
            SpamWarningColor = spamWarning,
            DisabledColor = disabled,
            UpdateBannerBgColor = updateBannerBg,
            UpdateBannerTextColor = updateBannerText,
            NoticeBorderColor = noticeBorder,
            NoticeTextColor = noticeText,
            NoticeButtonColor = noticeButton,
            NoticeButtonHoveredColor = noticeButtonHover,
            HpBarColor = hpBar,
            ShieldBarColor = shieldBar,
            BarBackgroundColor = barBackground,
            BarBorderColor = barBorder,
            OverkillColor = overkill,
            ModernShellColor = shell,
            ModernPanelColor = panel,
            ModernPanelAltColor = panelAlt,
            ModernPanelBorderColor = border,
            ModernAccentColor = accent,
            ModernAccentSoftColor = accentSoft,
            ModernMutedTextColor = muted,
            ModernTextColor = text,
            ModernDividerColor = divider,
            ModernFrameColor = frame,
            ModernFrameHoveredColor = frameHovered,
            ModernButtonHoveredColor = buttonHover,
            ModernNavButtonColor = button,
            ModernNavButtonHoveredColor = buttonHover,
            ModernNavButtonSelectedColor = selectedButton,
            ModernNavButtonSelectedHoveredColor = selectedButtonHover,
            ModernNavButtonActiveColor = buttonActive,
            ModernButtonTextColor = buttonText,
            ModernSelectedButtonTextColor = selectedButtonText,
            ModernPopupBgColor = popup,
            ModernCheckMarkColor = checkboxCheckMark,
            CheckboxFrameColor = checkboxFrame,
            CheckboxFrameHoveredColor = checkboxFrameHovered,
            CheckboxFrameActiveColor = checkboxFrameActive,
            CheckboxBorderColor = checkboxBorder,
            ModernSliderGrabColor = sliderGrab,
            ModernSliderGrabActiveColor = sliderGrabActive,
            ModernHeaderColor = header,
            ModernHeaderHoveredColor = headerHover,
            ModernHeaderActiveColor = headerActive,
            TableRowAltColor = tableRowAlt,
            FocusedRowColor = focusedRow,
            FocusedRowAccentColor = focusedRowAccent,
            ModernFrameBorderSize = baseTheme.ModernFrameBorderSize,
            TimelineSelectedRowColor = timelineSelected,
            TimelinePressedRowColor = timelinePressed,
            ScrollbarBackgroundColor = scrollbarBackground,
            ScrollbarGrabColor = scrollbarGrab,
            ScrollbarGrabHoveredColor = scrollbarGrabHover,
            ScrollbarGrabActiveColor = scrollbarGrabActive,
            ChangelogTabColor = changelogTab,
            ChangelogTabHoveredColor = changelogTabHover,
            ChangelogTabActiveColor = changelogTabActive,
            WidgetWindowBackgroundColor = widgetWindow,
            WidgetTitleBackgroundColor = widgetTitle,
            WidgetTitleActiveBackgroundColor = widgetTitleActive,
            WidgetBorderColor = widgetBorder,
            WidgetResizeGripColor = widgetResizeGrip,
            WidgetResizeGripHoveredColor = widgetResizeGripHover,
            WidgetResizeGripActiveColor = widgetResizeGripActive,
        };
    }

    private static ThemeColorValue ToThemeColorValue(Vector4 color)
    {
        return new ThemeColorValue(color.X, color.Y, color.Z, color.W);
    }

    private static Vector4 ToVector4(ThemeColorValue? color, Vector4 fallback)
    {
        if (color is null)
        {
            return fallback;
        }

        return new Vector4(
            Math.Clamp(color.R, 0.0f, 1.0f),
            Math.Clamp(color.G, 0.0f, 1.0f),
            Math.Clamp(color.B, 0.0f, 1.0f),
            Math.Clamp(color.A, 0.0f, 1.0f));
    }

    private static float GetColorLuminance(Vector4 color)
    {
        return (color.X * 0.2126f) + (color.Y * 0.7152f) + (color.Z * 0.0722f);
    }

    private static float GetContrastChannel(float channel)
    {
        return channel <= 0.03928f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private static float GetContrastLuminance(Vector4 color)
    {
        return (GetContrastChannel(color.X) * 0.2126f) +
            (GetContrastChannel(color.Y) * 0.7152f) +
            (GetContrastChannel(color.Z) * 0.0722f);
    }

    private static float GetColorContrast(Vector4 first, Vector4 second)
    {
        var firstLuminance = GetContrastLuminance(first);
        var secondLuminance = GetContrastLuminance(second);
        return (MathF.Max(firstLuminance, secondLuminance) + 0.05f) /
            (MathF.Min(firstLuminance, secondLuminance) + 0.05f);
    }

    private static float GetMinimumColorContrast(Vector4 color, params Vector4[] backgrounds)
    {
        if (backgrounds.Length == 0)
        {
            return float.MaxValue;
        }

        var minimumContrast = float.MaxValue;
        foreach (var background in backgrounds)
        {
            minimumContrast = MathF.Min(minimumContrast, GetColorContrast(color, background));
        }

        return minimumContrast;
    }

    private static bool MeetsMinimumContrast(Vector4 color, float minimumContrast, params Vector4[] backgrounds)
    {
        return GetMinimumColorContrast(color, backgrounds) >= minimumContrast;
    }

    private static Vector4 EnsureReadableTextColor(Vector4 desired, float minimumContrast, params Vector4[] backgrounds)
    {
        if (MeetsMinimumContrast(desired, minimumContrast, backgrounds))
        {
            return desired;
        }

        var averageLuminance = 0.0f;
        foreach (var background in backgrounds)
        {
            averageLuminance += GetColorLuminance(background);
        }

        averageLuminance /= MathF.Max(1.0f, backgrounds.Length);
        var primaryTarget = averageLuminance >= 0.55f
            ? new Vector4(0.0f, 0.0f, 0.0f, desired.W)
            : new Vector4(0.98f, 0.98f, 0.96f, desired.W);
        var secondaryTarget = averageLuminance >= 0.55f
            ? new Vector4(0.98f, 0.98f, 0.96f, desired.W)
            : new Vector4(0.0f, 0.0f, 0.0f, desired.W);
        var best = desired;
        var bestContrast = GetMinimumColorContrast(best, backgrounds);

        foreach (var target in new[] { primaryTarget, secondaryTarget })
        {
            for (var step = 1; step <= 20; step++)
            {
                var candidate = BlendColors(desired, target, step / 20.0f) with { W = desired.W };
                var candidateContrast = GetMinimumColorContrast(candidate, backgrounds);
                if (candidateContrast >= minimumContrast)
                {
                    return candidate;
                }

                if (candidateContrast > bestContrast)
                {
                    best = candidate;
                    bestContrast = candidateContrast;
                }
            }
        }

        return best;
    }

    private static Vector4 BlendColors(Vector4 first, Vector4 second, float amount)
    {
        var clampedAmount = Math.Clamp(amount, 0.0f, 1.0f);
        return new Vector4(
            first.X + ((second.X - first.X) * clampedAmount),
            first.Y + ((second.Y - first.Y) * clampedAmount),
            first.Z + ((second.Z - first.Z) * clampedAmount),
            first.W + ((second.W - first.W) * clampedAmount));
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
        Vector4? damage = null,
        Vector4? heal = null,
        Vector4? frame = null,
        Vector4? frameHovered = null,
        Vector4? popup = null,
        Vector4? navButton = null,
        Vector4? navButtonHovered = null,
        Vector4? navButtonSelected = null,
        Vector4? navButtonSelectedHovered = null,
        Vector4? navButtonActive = null,
        float frameBorderSize = 0.0f,
        Vector4? divider = null,
        Vector4? barBackground = null,
        Vector4? barBorder = null)
    {
        var dangerColor = danger ?? new Vector4(1.0f, 0.25f, 0.20f, 1.0f);
        var textColor = text ?? new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        var frameColor = frame ?? panelAlt;
        var frameHoveredColor = frameHovered ?? panelAlt with { W = 1.0f };
        var popupBgColor = popup ?? panel with { W = 0.98f };
        var lightPanels = GetColorLuminance(panel) >= 0.55f;
        var navButtonColor = navButton ?? panelAlt with { W = 0.96f };
        var navButtonHoveredColor = navButtonHovered ?? (lightPanels
            ? BlendColors(panelAlt, border, 0.10f) with { W = 1.0f }
            : border with { W = 1.0f });
        var navButtonSelectedColor = navButtonSelected ?? accentSoft with { W = 0.95f };
        var navButtonSelectedHoveredColor = navButtonSelectedHovered ?? (lightPanels
            ? BlendColors(accentSoft, panelAlt, 0.56f) with { W = 1.0f }
            : accentSoft with { W = 1.0f });
        var selectedButtonTextColor = lightPanels
            ? textColor
            : accent;
        var checkboxFrameColor = lightPanels
            ? BlendColors(frameColor, border, 0.35f) with { W = 1.0f }
            : BlendColors(frameColor, border, 0.42f) with { W = 0.96f };
        var checkboxFrameHoveredColor = lightPanels
            ? BlendColors(frameHoveredColor, border, 0.25f) with { W = 1.0f }
            : BlendColors(frameHoveredColor, accentSoft, 0.28f) with { W = 1.0f };
        var checkboxFrameActiveColor = lightPanels
            ? BlendColors(accentSoft, border, 0.28f) with { W = 1.0f }
            : accentSoft with { W = 1.0f };
        var checkboxBorderColor = lightPanels
            ? BlendColors(border, textColor, 0.14f) with { W = 1.0f }
            : BlendColors(border, accent, 0.18f) with { W = 1.0f };
        var tableRowAltColor = BlendColors(panel, panelAlt, 0.58f) with { W = 0.46f };
        var focusedRowColor = lightPanels
            ? BlendColors(panel, panelAlt, 0.62f) with { W = 0.42f }
            : BlendColors(panel, panelAlt, 0.58f) with { W = 0.46f };
        var focusedRowAccentColor = lightPanels
            ? BlendColors(border, accent, 0.22f) with { W = 0.82f }
            : BlendColors(border, accent, 0.32f) with { W = 0.74f };
        var scrollbarBackgroundColor = lightPanels
            ? BlendColors(panel, border, 0.16f) with { W = 0.76f }
            : BlendColors(shell, panel, 0.58f) with { W = 0.70f };
        var scrollbarGrabColor = lightPanels
            ? BlendColors(border, accent, 0.30f) with { W = 0.84f }
            : BlendColors(border, accent, 0.40f) with { W = 0.78f };
        var scrollbarGrabHoveredColor = BlendColors(scrollbarGrabColor, accent, 0.34f) with { W = 0.94f };
        var scrollbarGrabActiveColor = BlendColors(accent, textColor, lightPanels ? 0.08f : 0.04f) with { W = 1.0f };
        var modernButtonHoveredColor = lightPanels
            ? BlendColors(panelAlt, border, 0.10f) with { W = 1.0f }
            : border with { W = 1.0f };
        var timelineSelectedRowColor = accentSoft with { W = 0.48f };
        var timelinePressedRowColor = accentSoft with { W = 0.72f };
        var textSurfaces = new[]
        {
            shell,
            panel,
            panelAlt,
            frameColor,
            frameHoveredColor,
            popupBgColor,
            tableRowAltColor,
            focusedRowColor,
            timelineSelectedRowColor,
            timelinePressedRowColor,
        };
        var navButtonTextColor = EnsureReadableTextColor(
            textColor,
            4.5f,
            navButtonColor,
            navButtonHoveredColor,
            modernButtonHoveredColor,
            frameColor,
            frameHoveredColor);
        var navSelectedTextColor = EnsureReadableTextColor(
            selectedButtonTextColor,
            4.5f,
            navButtonSelectedColor,
            navButtonSelectedHoveredColor,
            navButtonActive ?? navButtonSelectedHoveredColor);
        var warningBaseColor = lightPanels
            ? new Vector4(0.52f, 0.28f, 0.0f, 1.0f)
            : new Vector4(1.0f, 0.68f, 0.28f, 1.0f);
        var disabledBaseColor = lightPanels
            ? BlendColors(textColor, panel, 0.34f) with { W = 1.0f }
            : new Vector4(0.64f, 0.64f, 0.64f, 1.0f);
        var damageColor = EnsureReadableTextColor(damage ?? new Vector4(1.0f, 0.36f, 0.26f, 1.0f), 3.0f, textSurfaces);
        var healColor = EnsureReadableTextColor(heal ?? new Vector4(0.27f, 0.88f, 0.45f, 1.0f), 3.0f, textSurfaces);
        var warningColor = EnsureReadableTextColor(warningBaseColor, 3.0f, textSurfaces);
        var leadUpGoldColor = EnsureReadableTextColor(highlight, 3.0f, textSurfaces);
        var dangerTextColor = EnsureReadableTextColor(dangerColor, 3.0f, textSurfaces);
        var disabledColor = EnsureReadableTextColor(disabledBaseColor, 3.0f, textSurfaces);
        return new BetterDeathsUiTheme
        {
            Id = id,
            Label = label,
            DamageColor = damageColor,
            HealColor = healColor,
            WarningColor = warningColor,
            LeadUpGoldColor = leadUpGoldColor,
            SpamWarningColor = dangerTextColor,
            DisabledColor = disabledColor,
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
            OverkillColor = dangerTextColor,
            ModernShellColor = shell,
            ModernPanelColor = panel,
            ModernPanelAltColor = panelAlt,
            ModernPanelBorderColor = border,
            ModernAccentColor = accent,
            ModernAccentSoftColor = accentSoft,
            ModernMutedTextColor = muted,
            ModernTextColor = textColor,
            ModernDividerColor = divider ?? new Vector4(1.0f, 1.0f, 1.0f, 0.10f),
            ModernFrameColor = frameColor,
            ModernFrameHoveredColor = frameHoveredColor,
            ModernButtonHoveredColor = modernButtonHoveredColor,
            ModernNavButtonColor = navButtonColor,
            ModernNavButtonHoveredColor = navButtonHoveredColor,
            ModernNavButtonSelectedColor = navButtonSelectedColor,
            ModernNavButtonSelectedHoveredColor = navButtonSelectedHoveredColor,
            ModernNavButtonActiveColor = navButtonActive ?? navButtonSelectedHoveredColor,
            ModernButtonTextColor = navButtonTextColor,
            ModernSelectedButtonTextColor = navSelectedTextColor,
            ModernPopupBgColor = popupBgColor,
            ModernCheckMarkColor = accent,
            CheckboxFrameColor = checkboxFrameColor,
            CheckboxFrameHoveredColor = checkboxFrameHoveredColor,
            CheckboxFrameActiveColor = checkboxFrameActiveColor,
            CheckboxBorderColor = checkboxBorderColor,
            ModernSliderGrabColor = accent with { W = 0.72f },
            ModernSliderGrabActiveColor = accent,
            ModernHeaderColor = accentSoft with { W = 0.42f },
            ModernHeaderHoveredColor = frameHoveredColor,
            ModernHeaderActiveColor = accentSoft with { W = 1.0f },
            TableRowAltColor = tableRowAltColor,
            FocusedRowColor = focusedRowColor,
            FocusedRowAccentColor = focusedRowAccentColor,
            ModernFrameBorderSize = frameBorderSize,
            TimelineSelectedRowColor = timelineSelectedRowColor,
            TimelinePressedRowColor = timelinePressedRowColor,
            ScrollbarBackgroundColor = scrollbarBackgroundColor,
            ScrollbarGrabColor = scrollbarGrabColor,
            ScrollbarGrabHoveredColor = scrollbarGrabHoveredColor,
            ScrollbarGrabActiveColor = scrollbarGrabActiveColor,
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
