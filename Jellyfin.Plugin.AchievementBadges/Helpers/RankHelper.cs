using System.Collections.Generic;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

public static class RankHelper
{
    public record RankTier(string Name, int MinScore, string Color, string Icon);

    public static readonly IReadOnlyList<RankTier> Tiers = new List<RankTier>
    {
        new("Rookie",       0,    "#9aa5b1", "sprout"),
        new("Novice",       100,  "#4caf50", "eco"),
        new("Viewer",       300,  "#2196f3", "visibility"),
        new("Regular",      700,  "#03a9f4", "person"),
        new("Enthusiast",   1500, "#00bcd4", "star"),
        new("Binger",       3000, "#9c27b0", "bolt"),
        new("Connoisseur",  5000, "#e91e63", "workspace_premium"),
        new("Maestro",      8000, "#ff9800", "military_tech"),
        new("Legend",       12000, "#f44336", "local_fire_department"),
        new("Immortal",     20000, "#ffd700", "auto_awesome")
    };

    public static RankTier GetTier(int score)
    {
        RankTier current = Tiers[0];
        foreach (var tier in Tiers)
        {
            if (score >= tier.MinScore)
            {
                current = tier;
            }
            else
            {
                break;
            }
        }
        return current;
    }

    public static RankTier? GetNextTier(int score)
    {
        for (var i = 0; i < Tiers.Count; i++)
        {
            if (score < Tiers[i].MinScore)
            {
                return Tiers[i];
            }
        }
        return null;
    }
}
