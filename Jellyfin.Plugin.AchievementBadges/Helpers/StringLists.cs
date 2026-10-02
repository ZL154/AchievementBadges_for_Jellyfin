using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

/// <summary>
/// Genres, tags and studios come from a Jellyfin item and from its parent
/// series, and both live playback and the watch-history backfill merge the
/// two the same way, so they share this.
/// </summary>
public static class StringLists
{
    /// <summary>
    /// Both lists in one, without blank entries and without names that differ
    /// only by case. Null when nothing is left, so PlaybackContext.Genres,
    /// Tags and Studios keep the nullable shape callers gate on.
    /// </summary>
    public static IReadOnlyList<string>? Union(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if ((a is null || a.Count == 0) && (b is null || b.Count == 0)) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (a is not null) foreach (var s in a) if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        if (b is not null) foreach (var s in b) if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        return set.Count == 0 ? null : new List<string>(set);
    }
}
