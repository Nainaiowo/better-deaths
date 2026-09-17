using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace BetterDeaths;

public sealed class CustomThemeCacheTests
{
    private static readonly PropertyInfo[] Colors = typeof(CustomThemeConfiguration).GetProperties()
        .Where(p => p.PropertyType == typeof(ThemeColorValue)).ToArray();

    public static IEnumerable<object[]> ColorEdits => Colors.SelectMany(color =>
        new[] { "R", "G", "B", "A" }.SelectMany(channel => new[] { false, true }
            .Select(replace => new object[] { color.Name, channel, replace })));

    public static IEnumerable<object[]> ColorNames => Colors.Select(color => new object[] { color.Name });

    [Theory]
    [MemberData(nameof(ColorEdits))]
    public void EveryColorChannelRefreshesImmediatelyWithoutSaving(string property, string channel, bool replace)
    {
        var preset = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura);
        var custom = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(preset);
        var before = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
        var colorProperty = typeof(CustomThemeConfiguration).GetProperty(property)!;
        var color = (ThemeColorValue)colorProperty.GetValue(custom)!;
        if (replace)
        {
            color = new ThemeColorValue(color.R, color.G, color.B, color.A);
            colorProperty.SetValue(custom, color);
        }
        var valueProperty = typeof(ThemeColorValue).GetProperty(channel)!;
        var oldValue = (float)valueProperty.GetValue(color)!;
        var nextValue = oldValue == .125f ? .875f : .125f;
        valueProperty.SetValue(color, nextValue);

        var updated = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
        Assert.NotSame(before, updated);
        Assert.Same(updated, BetterDeathsThemeCatalog.GetTheme(preset.Id, custom));
        Assert.Equal(preset.Id, updated.Id);
        Assert.Equal(preset.Decoration, updated.Decoration);
        Assert.Equal(preset.WidgetSurface, updated.WidgetSurface);
        Assert.Equal(preset.ModernFrameBorderSize, updated.ModernFrameBorderSize);
        AssertColorsEqual(custom, BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(updated));
    }

    [Theory]
    [MemberData(nameof(ColorNames))]
    public void MissingColorsReturnToTheSelectedPreset(string property)
    {
        var preset = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection);
        var custom = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(preset);
        var color = typeof(CustomThemeConfiguration).GetProperty(property)!;
        color.SetValue(custom, new ThemeColorValue(.125f, .125f, .125f, .125f));
        var before = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
        color.SetValue(custom, null);
        var updated = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
        Assert.NotSame(before, updated);
        AssertColorsEqual(BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(preset),
            BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(updated));
    }

    [Fact]
    public void PresetChangesAndDisablingOrReplacingCustomColorsRemainCorrect()
    {
        var custom = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura));
        var first = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura, custom);
        var changedBase = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom);
        Assert.NotSame(first, changedBase);
        Assert.Equal("Confection Custom", changedBase.Label);
        Assert.Equal(WidgetSurfaceStyle.Confection, changedBase.WidgetSurface);
        Assert.Equal(ThemeDecoration.Sweets, changedBase.Decoration);
        AssertColorsEqual(custom, BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(changedBase));

        custom.Enabled = false;
        Assert.Same(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection),
            BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom));
        custom.Enabled = true;
        custom.Initialized = false;
        Assert.Same(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection),
            BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom));
        custom.Initialized = true;
        custom.Accent.R = .125f;
        Assert.Equal(.125f, BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom).ModernAccentColor.X);

        var serialized = JsonSerializer.Serialize(custom);
        var replacement = JsonSerializer.Deserialize<CustomThemeConfiguration>(serialized)!;
        AssertColorsEqual(custom, replacement);
        Assert.Same(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom),
            BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, replacement));
        replacement.Accent.R = .875f;
        Assert.Equal(.875f, BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, replacement).ModernAccentColor.X);
        Assert.Equal(.125f, BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection, custom).ModernAccentColor.X);
    }

    [Fact]
    public void AllPresetsAndOutOfRangeValuesRetainExistingResolutionRules()
    {
        Assert.Equal(34, BetterDeathsThemeCatalog.All.Count);
        Assert.All(BetterDeathsThemeCatalog.All, preset =>
        {
            var custom = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(preset);
            var resolved = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
            AssertColorsEqual(custom, BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(resolved));
            Assert.Same(resolved, BetterDeathsThemeCatalog.GetTheme(preset.Id, custom));
            custom.Accent = new ThemeColorValue(-3, 4, 2, -1);
            var clamped = BetterDeathsThemeCatalog.GetTheme(preset.Id, custom);
            Assert.Equal(new Vector4(0, 1, 1, 0), clamped.ModernAccentColor);
            Assert.Same(clamped, BetterDeathsThemeCatalog.GetTheme(preset.Id, custom));
        });
    }

    [Fact]
    public void UnchangedPaletteReadsAllocateNothing()
    {
        var custom = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura));
        var expected = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura, custom);
        for (var i = 0; i < 100; i++) _ = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura, custom);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) _ = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura, custom);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - bytes);
        Assert.Same(expected, BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura, custom));
    }

    [Fact]
    public void ConcurrentReadersNeverReceiveAnotherPalettesColors()
    {
        var a = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Sakura));
        var b = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Confection));
        a.Accent.R = .125f;
        b.Accent.R = .875f;
        Parallel.For(0, 1000, i =>
        {
            var result = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Classic, i % 2 == 0 ? a : b);
            Assert.Equal(i % 2 == 0 ? .125f : .875f, result.ModernAccentColor.X);
        });
    }

    private static void AssertColorsEqual(CustomThemeConfiguration expected, CustomThemeConfiguration actual)
    {
        foreach (var property in Colors)
        {
            var a = (ThemeColorValue)property.GetValue(expected)!;
            var b = (ThemeColorValue)property.GetValue(actual)!;
            Assert.Equal(new Vector4(a.R, a.G, a.B, a.A), new Vector4(b.R, b.G, b.B, b.A));
        }
    }
}
