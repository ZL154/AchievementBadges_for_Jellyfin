using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

/// <summary>
/// [issue #129] Member ids of the collection and playlist targets, so the
/// per-play membership check is a set test for those too.
/// <para>
/// Jellyfin indexes the real hierarchy (series, season, folder), which is what
/// the ancestor test uses, but a collection or playlist holds its members as
/// linked children on the container itself, with no reverse index from an item
/// to the containers that link it. So the plugin keeps that set per target:
/// built on first use from the container's linked children, dropped when
/// Jellyfin reports the container changed (adding or removing a member updates
/// the container item), and rebuilt after <see cref="Ttl"/> regardless, as a
/// safety net for an event that never arrived. The full pass on badge save and
/// backfill stays the reconciler for anything in between.
/// </para>
/// </summary>
public sealed class LinkedTargetMembers
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly Func<DateTime> _clock;

    public LinkedTargetMembers(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>How many containers are cached right now.</summary>
    public int Count => _entries.Count;

    /// <summary>Whether <paramref name="itemId"/> is a member of the container,
    /// building the set through <paramref name="build"/> when it is missing or
    /// older than <see cref="Ttl"/>.</summary>
    public bool Contains(Guid containerId, Guid itemId, Func<IEnumerable<Guid>> build)
    {
        var now = _clock();
        if (!_entries.TryGetValue(containerId, out var entry) || now - entry.BuiltAt >= Ttl)
        {
            entry = new Entry(new HashSet<Guid>(build()), now);
            _entries[containerId] = entry;
        }

        return entry.Ids.Contains(itemId);
    }

    /// <summary>Drop one container's set; the next play rebuilds it.</summary>
    public bool Invalidate(Guid containerId)
    {
        return _entries.TryRemove(containerId, out _);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private sealed record Entry(HashSet<Guid> Ids, DateTime BuiltAt);
}
