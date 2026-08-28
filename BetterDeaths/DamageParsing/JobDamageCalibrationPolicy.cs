namespace BetterDeaths.DamageParsing;

using System.Collections.Generic;
using System.Linq;

internal static class JobDamageCalibrationPolicy
{
    internal const uint MachinistOverheatedStatusId = 0xA80;

    private const uint MachinistClassJobId = 31;

    private static readonly HashSet<uint> BlackMageClassJobIds = [7, 25];

    // These spells are changed by Astral Fire or Umbral Ice, which live in the
    // Black Mage job gauge rather than a status snapshot available for every player.
    private static readonly HashSet<uint> BlackMageElementalActionIds =
    [
        0x008D, // Fire
        0x008E, // Blizzard
        0x0092, // Blizzard II
        0x0093, // Fire II
        0x0098, // Fire III
        0x009A, // Blizzard III
        0x009F, // Freeze
        0x00A2, // Flare
        0x0DF8, // Blizzard IV
        0x0DF9, // Fire IV
        0x4079, // Despair
        0x64C2, // High Fire II
        0x64C3, // High Blizzard II
        0x907D, // Flare Star
    ];

    // Overheated adds 20 potency to this set. Pet actions stay out of direct
    // potency calibration and therefore do not belong here.
    private static readonly HashSet<uint> MachinistOverheatedActionIds =
    [
        0x00B32, // Split Shot
        0x00B34, // Slug Shot
        0x00B38, // Hot Shot
        0x00B3A, // Gauss Round
        0x00B39, // Clean Shot
        0x01CF2, // Heat Blast
        0x01CF3, // Heated Split Shot
        0x04072, // Drill
        0x01CF4, // Heated Slug Shot
        0x01CF5, // Heated Clean Shot
        0x09072, // Blazing Shot
        0x04074, // Air Anchor
    ];

    public static bool IsRelevantStatus(uint statusId)
    {
        return statusId == MachinistOverheatedStatusId;
    }

    public static double GetDefaultDurationSeconds(uint statusId)
    {
        return statusId == MachinistOverheatedStatusId ? 10.0 : 20.0;
    }

    public static double? GetCalibrationPotency(ParsedDamageEvent damageEvent)
    {
        if (!damageEvent.CanCalibratePotency || damageEvent.DirectPotency is not > 0.0)
        {
            return null;
        }

        var source = damageEvent.AttributedSource ?? damageEvent.Source;
        if (BlackMageClassJobIds.Contains(source.ClassJobId) &&
            BlackMageElementalActionIds.Contains(damageEvent.ActionId))
        {
            return null;
        }

        var potency = damageEvent.DirectPotency.Value;
        if (source.ClassJobId == MachinistClassJobId &&
            MachinistOverheatedActionIds.Contains(damageEvent.ActionId) &&
            damageEvent.SourceStatuses.Any(status =>
                status.StatusId == MachinistOverheatedStatusId && status.RemainingTime > 0.0f))
        {
            potency += 20.0;
        }

        return potency;
    }
}
