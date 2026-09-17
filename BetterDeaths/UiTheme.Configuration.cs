namespace BetterDeaths;

internal static partial class BetterDeathsThemeCatalog
{
    public static BetterDeathsUiTheme GetTheme(Configuration configuration) =>
        GetTheme(configuration.Theme, configuration.CustomTheme);
}
