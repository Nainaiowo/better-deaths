using System;

namespace BetterDeaths;

[Serializable]
public sealed class ThemeColorValue
{
    public float R { get; set; }

    public float G { get; set; }

    public float B { get; set; }

    public float A { get; set; } = 1.0f;

    public ThemeColorValue()
    {
    }

    public ThemeColorValue(float r, float g, float b, float a = 1.0f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

[Serializable]
public sealed class CustomThemeConfiguration
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public bool Enabled { get; set; }

    public bool Initialized { get; set; }

    public ThemeColorValue WindowBackground { get; set; } = new();

    public ThemeColorValue ContentBackground { get; set; } = new();

    public ThemeColorValue RaisedBackground { get; set; } = new();

    public ThemeColorValue Border { get; set; } = new();

    public ThemeColorValue Divider { get; set; } = new();

    public ThemeColorValue Accent { get; set; } = new();

    public ThemeColorValue AccentSoft { get; set; } = new();

    public ThemeColorValue RegularText { get; set; } = new();

    public ThemeColorValue MutedText { get; set; } = new();

    public ThemeColorValue GoldText { get; set; } = new();

    public ThemeColorValue DisabledText { get; set; } = new();

    public ThemeColorValue DamageText { get; set; } = new();

    public ThemeColorValue HealText { get; set; } = new();

    public ThemeColorValue WarningText { get; set; } = new();

    public ThemeColorValue SpamWarningText { get; set; } = new();

    public ThemeColorValue OverkillText { get; set; } = new();

    public ThemeColorValue FrameBackground { get; set; } = new();

    public ThemeColorValue FrameHoverBackground { get; set; } = new();

    public ThemeColorValue PopupBackground { get; set; } = new();

    public ThemeColorValue ButtonColor { get; set; } = new();

    public ThemeColorValue ButtonHoverColor { get; set; } = new();

    public ThemeColorValue SelectedButtonColor { get; set; } = new();

    public ThemeColorValue SelectedButtonHoverColor { get; set; } = new();

    public ThemeColorValue ButtonActiveColor { get; set; } = new();

    public ThemeColorValue ButtonText { get; set; } = new();

    public ThemeColorValue SelectedButtonText { get; set; } = new();

    public ThemeColorValue CheckboxBackground { get; set; } = new();

    public ThemeColorValue CheckboxHoverBackground { get; set; } = new();

    public ThemeColorValue CheckboxActiveBackground { get; set; } = new();

    public ThemeColorValue CheckboxCheckMark { get; set; } = new();

    public ThemeColorValue CheckboxBorder { get; set; } = new();

    public ThemeColorValue SliderGrab { get; set; } = new();

    public ThemeColorValue SliderGrabActive { get; set; } = new();

    public ThemeColorValue HeaderBackground { get; set; } = new();

    public ThemeColorValue HeaderHoverBackground { get; set; } = new();

    public ThemeColorValue HeaderActiveBackground { get; set; } = new();

    public ThemeColorValue TableRowAlt { get; set; } = new();

    public ThemeColorValue FocusedRow { get; set; } = new();

    public ThemeColorValue FocusedRowAccent { get; set; } = new();

    public ThemeColorValue TimelineSelectedRow { get; set; } = new();

    public ThemeColorValue TimelinePressedRow { get; set; } = new();

    public ThemeColorValue ScrollbarBackground { get; set; } = new();

    public ThemeColorValue ScrollbarGrab { get; set; } = new();

    public ThemeColorValue ScrollbarGrabHover { get; set; } = new();

    public ThemeColorValue ScrollbarGrabActive { get; set; } = new();

    public ThemeColorValue ChangelogTab { get; set; } = new();

    public ThemeColorValue ChangelogTabHover { get; set; } = new();

    public ThemeColorValue ChangelogTabActive { get; set; } = new();

    public ThemeColorValue HpBar { get; set; } = new();

    public ThemeColorValue ShieldBar { get; set; } = new();

    public ThemeColorValue BarBackground { get; set; } = new();

    public ThemeColorValue BarBorder { get; set; } = new();

    public ThemeColorValue WidgetWindowBackground { get; set; } = new();

    public ThemeColorValue WidgetTitleBackground { get; set; } = new();

    public ThemeColorValue WidgetTitleActiveBackground { get; set; } = new();

    public ThemeColorValue WidgetBorder { get; set; } = new();

    public ThemeColorValue WidgetResizeGrip { get; set; } = new();

    public ThemeColorValue WidgetResizeGripHover { get; set; } = new();

    public ThemeColorValue WidgetResizeGripActive { get; set; } = new();

    public ThemeColorValue UpdateBannerBackground { get; set; } = new();

    public ThemeColorValue UpdateBannerText { get; set; } = new();

    public ThemeColorValue NoticeBorder { get; set; } = new();

    public ThemeColorValue NoticeText { get; set; } = new();

    public ThemeColorValue NoticeButton { get; set; } = new();

    public ThemeColorValue NoticeButtonHover { get; set; } = new();
}
