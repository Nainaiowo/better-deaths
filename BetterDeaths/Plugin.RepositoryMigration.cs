using BetterDeaths.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private enum RepositoryMigrationResult
    {
        NotFound,
        DevPlugin,
        UnexpectedSource,
        AlreadyMigrated,
        Migrated,
    }

    private async Task MigrateToPuniRepositoryAsync()
    {
        if (Configuration.PuniRepositoryMigrationComplete)
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(PuniDalamudRepositoryUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug(
                    "Better Deaths Puni repository migration skipped because {RepositoryUrl} returned {StatusCode}.",
                    PuniDalamudRepositoryUrl,
                    response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!PuniRepositoryContainsInstallableBetterDeaths(json))
            {
                Log.Debug("Better Deaths Puni repository migration skipped because the Puni feed does not contain an installable BetterDeaths release yet.");
                return;
            }

            TryAddDalamudRepository(PuniDalamudRepositoryUrl);
            var migrationResult = TryMigrateInstalledPluginRepository();
            if (migrationResult is RepositoryMigrationResult.Migrated or RepositoryMigrationResult.AlreadyMigrated)
            {
                Configuration.PuniRepositoryMigrationComplete = true;
                SaveConfiguration();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration failed.");
        }
    }

    private static bool PuniRepositoryContainsInstallableBetterDeaths(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var plugin in document.RootElement.EnumerateArray())
            {
                if (!TryGetJsonString(plugin, "InternalName", out var internalName) ||
                    !string.Equals(internalName, BetterDeathsInternalName, StringComparison.Ordinal))
                {
                    continue;
                }

                return TryGetJsonString(plugin, "AssemblyVersion", out var assemblyVersion) &&
                    !string.IsNullOrWhiteSpace(assemblyVersion) &&
                    TryGetJsonString(plugin, "DownloadLinkInstall", out var installLink) &&
                    !string.IsNullOrWhiteSpace(installLink) &&
                    TryGetJsonString(plugin, "DownloadLinkUpdate", out var updateLink) &&
                    !string.IsNullOrWhiteSpace(updateLink);
            }
        }
        catch (JsonException ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not parse the Puni feed.");
        }

        return false;
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void TryAddDalamudRepository(string repositoryUrl)
    {
        try
        {
            var assembly = typeof(IDalamudPlugin).Assembly;
            var config = GetDalamudService(assembly, "Dalamud.Configuration.Internal.DalamudConfiguration");
            if (config is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find DalamudConfiguration.");
                return;
            }

            var repoList = GetPropertyValue(config, "ThirdRepoList") as IList;
            if (repoList is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find ThirdRepoList.");
                return;
            }

            var changed = false;
            foreach (var repo in repoList)
            {
                var existingUrl = GetStringProperty(repo, "Url");
                if (!string.Equals(existingUrl, repositoryUrl, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changed |= SetBoolProperty(repo, "IsEnabled", true);
                SaveDalamudRepositoryConfiguration(assembly, config, changed);
                return;
            }

            var repoType = assembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings", throwOnError: false);
            if (repoType is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find ThirdPartyRepoSettings.");
                return;
            }

            var newRepo = Activator.CreateInstance(repoType);
            if (newRepo is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not create ThirdPartyRepoSettings.");
                return;
            }

            SetStringProperty(newRepo, "Url", repositoryUrl);
            SetBoolProperty(newRepo, "IsEnabled", true);
            repoList.Add(newRepo);
            SaveDalamudRepositoryConfiguration(assembly, config, changed: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not add the Puni repository.");
        }
    }

    private static RepositoryMigrationResult TryMigrateInstalledPluginRepository()
    {
        try
        {
            var assembly = typeof(IDalamudPlugin).Assembly;
            var manager = GetDalamudService(assembly, "Dalamud.Plugin.Internal.PluginManager");
            var plugins = GetPropertyValue(manager, "InstalledPlugins") as IEnumerable;
            if (plugins is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find InstalledPlugins.");
                return RepositoryMigrationResult.NotFound;
            }

            foreach (var plugin in plugins)
            {
                if (!string.Equals(GetStringProperty(plugin, "InternalName"), BetterDeathsInternalName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (GetBoolProperty(plugin, "IsDev") == true)
                {
                    return RepositoryMigrationResult.DevPlugin;
                }

                var manifest = GetFieldValue(plugin, "manifest") ?? GetPropertyValue(plugin, "Manifest");
                if (manifest is null)
                {
                    Log.Debug("Better Deaths Puni repository migration found BetterDeaths but could not read its manifest.");
                    return RepositoryMigrationResult.NotFound;
                }

                var installedFromUrl = GetStringProperty(manifest, "InstalledFromUrl");
                if (string.Equals(installedFromUrl, PuniDalamudRepositoryUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return RepositoryMigrationResult.AlreadyMigrated;
                }

                if (string.IsNullOrWhiteSpace(installedFromUrl) ||
                    (!installedFromUrl.Contains("IMakeSillyThings", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(installedFromUrl, LegacyDalamudRepositoryUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Debug(
                        "Better Deaths Puni repository migration did not rewrite unexpected install URL: {InstalledFromUrl}",
                        installedFromUrl ?? string.Empty);
                    return RepositoryMigrationResult.UnexpectedSource;
                }

                SetStringProperty(manifest, "InstalledFromUrl", PuniDalamudRepositoryUrl);
                InvokeMethod(plugin, "SaveManifest", "Migrated to Puni.sh repository");
                Log.Information("Better Deaths migrated installed repository URL to Puni.sh.");
                return RepositoryMigrationResult.Migrated;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not rewrite the installed plugin source.");
        }

        return RepositoryMigrationResult.NotFound;
    }

    private static object? GetDalamudService(Assembly assembly, string serviceTypeName)
    {
        var serviceType = assembly.GetType("Dalamud.Service`1", throwOnError: false);
        var requestedType = assembly.GetType(serviceTypeName, throwOnError: false);
        if (serviceType is null || requestedType is null)
        {
            return null;
        }

        return serviceType
            .MakeGenericType(requestedType)
            .GetMethod("Get", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static void SaveDalamudRepositoryConfiguration(Assembly assembly, object config, bool changed)
    {
        if (!changed)
        {
            return;
        }

        InvokeMethod(config, "QueueSave");
        var manager = GetDalamudService(assembly, "Dalamud.Plugin.Internal.PluginManager");
        InvokeMethod(manager, "SetPluginReposFromConfigAsync", true);
    }

    private static object? GetPropertyValue(object? instance, string propertyName)
    {
        return instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance);
    }

    private static object? GetFieldValue(object? instance, string fieldName)
    {
        return instance?.GetType()
            .GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance);
    }

    private static string? GetStringProperty(object? instance, string propertyName)
    {
        return GetPropertyValue(instance, propertyName) as string;
    }

    private static bool? GetBoolProperty(object? instance, string propertyName)
    {
        return GetPropertyValue(instance, propertyName) as bool?;
    }

    private static bool SetStringProperty(object? instance, string propertyName, string value)
    {
        var property = instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.SetMethod is null)
        {
            return false;
        }

        var previous = property.GetValue(instance) as string;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }

    private static bool SetBoolProperty(object? instance, string propertyName, bool value)
    {
        var property = instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.SetMethod is null || property.PropertyType != typeof(bool))
        {
            return false;
        }

        var previous = property.GetValue(instance) as bool?;
        if (previous == value)
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }

    private static void InvokeMethod(object? instance, string methodName, params object[] parameters)
    {
        if (instance is null)
        {
            return;
        }

        instance.GetType()
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(instance, parameters);
    }
}
