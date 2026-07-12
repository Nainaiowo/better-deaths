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
    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var name = actionId == 0 ? "Unknown action" : "Auto";
        try
        {
            var action = DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            var sheetName = action?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load action name for {ActionId}.", actionId);
        }

        actionNameCache[actionId] = name;
        return name;
    }

    private string GetStatusSourceName(uint sourceId)
    {
        if (sourceId == 0)
        {
            return "Unknown source";
        }

        try
        {
            var sourceObject = ObjectTable.SearchByEntityId(sourceId);
            var name = sourceObject?.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve status source for {SourceId:X8}.", sourceId);
        }

        return $"Entity {sourceId:X8}";
    }

    private uint GetActionIconId(uint actionId)
    {
        if (actionIconCache.TryGetValue(actionId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var action = DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            iconId = action?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load action icon for {ActionId}.", actionId);
        }

        actionIconCache[actionId] = iconId;
        return iconId;
    }

    private string GetStatusName(uint statusId)
    {
        if (statusNameCache.TryGetValue(statusId, out var cachedName))
        {
            return cachedName;
        }

        var name = $"Status {statusId}";
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            var sheetName = status?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status name for {StatusId}.", statusId);
        }

        statusNameCache[statusId] = name;
        return name;
    }

    private uint GetStatusIconId(uint statusId)
    {
        if (statusIconCache.TryGetValue(statusId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            iconId = status?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status icon for {StatusId}.", statusId);
        }

        statusIconCache[statusId] = iconId;
        return iconId;
    }

    private string GetClassJobName(uint classJobId)
    {
        if (classJobNameCache.TryGetValue(classJobId, out var cachedName))
        {
            return cachedName;
        }

        var name = classJobId == 0 ? "Unknown job" : $"Job {classJobId}";
        try
        {
            var classJob = DataManager.GetExcelSheet<ClassJob>()?.GetRowOrDefault(classJobId);
            var abbreviation = classJob?.Abbreviation.ExtractText();
            if (!string.IsNullOrWhiteSpace(abbreviation))
            {
                name = abbreviation;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load class job name for {ClassJobId}.", classJobId);
        }

        classJobNameCache[classJobId] = name;
        return name;
    }

    private string GetTerritoryName(uint territoryId)
    {
        if (territoryNameCache.TryGetValue(territoryId, out var cachedName))
        {
            return cachedName;
        }

        var name = territoryId == 0 ? "Unknown territory" : $"Territory {territoryId}";
        try
        {
            var territory = DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
            var sheetName = territory?.PlaceName.ValueNullable?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load territory name for {TerritoryId}.", territoryId);
        }

        territoryNameCache[territoryId] = name;
        return name;
    }
}
