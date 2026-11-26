using System;
using System.Collections.Generic;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths;

public sealed partial class Configuration
{
    public bool ShowPartyCooldowns { get; set; }
    public bool PartyCooldownAutoCollapseJobs { get; set; } = true;
    public PartyCooldownSide PartyCooldownSide { get; set; } = PartyCooldownSide.Right;
    public PartyCooldownPlacements? PartyCooldownPlacements { get; set; }
    public Dictionary<uint, List<uint>> PartyCooldownActions { get; set; } = [];
    public Dictionary<uint, PartyCooldownRows> PartyCooldownRowLayouts { get; set; } = [];
    public int PartyCooldownDefaultLoadoutsVersion { get; set; }
    public PartyCooldownImportUndo? LastPartyCooldownImport { get; set; }

    // Legacy shared values are retained for migrating existing configurations.
    public float PartyCooldownIconSize { get; set; } = 24;
    public float PartyCooldownIconGap { get; set; } = 3;
    public float PartyCooldownOffsetX { get; set; }
    public float PartyCooldownOffsetY { get; set; }

    internal PartyCooldownPlacement GetPartyCooldownPlacement() => PartyCooldownPlacements!.Get(PartyCooldownSide);

    internal bool TrySaveCooldownImport(string? code, uint? targetJobId, PartyCooldownCatalog catalog,
        Action<Configuration> save, Action<Exception> reportSaveFailure, out bool includedLayout, out CooldownImportError error)
    {
        includedLayout = false;
        var candidate = CreateCooldownCandidate();
        if (!PartyCooldownLoadoutShareCodec.TryImport(code, targetJobId, catalog, candidate.PartyCooldownActions,
                candidate.PartyCooldownRowLayouts, out var layout, out error))
            return false;

        if (layout is not null)
        {
            var placements = new PartyCooldownPlacements
            {
                Left = FromSharePlacement(layout.Left!),
                Right = FromSharePlacement(layout.Right!),
            };
            candidate.SetCooldownPlacement(layout.Side, placements, placements.Get(layout.Side));
        }
        var undo = PartyCooldownImportUndo.Capture(this, catalog, targetJobId, layout is not null);
        if (!undo.CanRestoreExactly(catalog))
        {
            error = new(CooldownImportProblem.BackupFailed);
            return false;
        }
        candidate.LastPartyCooldownImport = undo;
        if (!TryCommitCooldownCandidate(candidate, layout is not null, save, reportSaveFailure))
        {
            error = new(CooldownImportProblem.SaveFailed);
            return false;
        }
        includedLayout = layout is not null;
        return true;
    }

    internal bool TryUndoLastCooldownImport(PartyCooldownCatalog catalog, Action<Configuration> save,
        Action<Exception> reportSaveFailure, out string error)
    {
        if (LastPartyCooldownImport is not { } undo)
        {
            error = "There is no cooldown import to undo.";
            return false;
        }
        if (!undo.CanRestoreExactly(catalog))
        {
            error = "The previous cooldown setup could not be restored exactly.";
            return false;
        }
        var candidate = CreateCooldownCandidate();
        undo.RestoreTo(candidate);
        candidate.LastPartyCooldownImport = null;
        var saved = TryCommitCooldownCandidate(candidate, undo.Layout is not null, save, reportSaveFailure);
        error = saved ? string.Empty : "Better Deaths couldn't save the restored settings. You can retry Undo.";
        return saved;
    }

    private Configuration CreateCooldownCandidate()
    {
        // Import and undo replace affected jobs; neither edits the live lists or rows in place.
        var candidate = (Configuration)MemberwiseClone();
        candidate.PartyCooldownActions = new(PartyCooldownActions);
        candidate.PartyCooldownRowLayouts = new(PartyCooldownRowLayouts);
        return candidate;
    }

    private bool TryCommitCooldownCandidate(Configuration candidate, bool includeLayout, Action<Configuration> save,
        Action<Exception> reportSaveFailure)
    {
        try
        {
            save(candidate);
        }
        catch (Exception exception)
        {
            reportSaveFailure(exception);
            return false;
        }

        // Keep the live Configuration instance used by other windows. Publish only what was saved.
        PartyCooldownActions = candidate.PartyCooldownActions;
        PartyCooldownRowLayouts = candidate.PartyCooldownRowLayouts;
        if (includeLayout)
            SetCooldownPlacement(candidate.PartyCooldownSide, candidate.PartyCooldownPlacements, candidate.GetLegacyCooldownPlacement());
        LastPartyCooldownImport = candidate.LastPartyCooldownImport;
        return true;
    }

    internal PartyCooldownPlacement GetLegacyCooldownPlacement() => new()
    {
        IconSize = PartyCooldownIconSize,
        Spacing = PartyCooldownIconGap,
        OffsetX = PartyCooldownOffsetX,
        OffsetY = PartyCooldownOffsetY,
    };

    internal void SetCooldownPlacement(PartyCooldownSide side, PartyCooldownPlacements? placements, PartyCooldownPlacement legacy)
    {
        PartyCooldownSide = side;
        PartyCooldownPlacements = placements;
        (PartyCooldownIconSize, PartyCooldownIconGap, PartyCooldownOffsetX, PartyCooldownOffsetY) =
            (legacy.IconSize, legacy.Spacing, legacy.OffsetX, legacy.OffsetY);
    }

    private static PartyCooldownPlacement FromSharePlacement(PartyCooldownLoadoutSharePlacement placement) => new()
    {
        IconSize = placement.IconSize,
        Spacing = placement.Spacing,
        OffsetX = placement.OffsetX,
        OffsetY = placement.OffsetY,
    };
}
