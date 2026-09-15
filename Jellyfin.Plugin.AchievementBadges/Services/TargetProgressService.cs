using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

/// <summary>[issue #107] One pass of targeted progress for one user.</summary>
public sealed class TargetProgressResult
{
    public Dictionary<string, int> ContainerPercents { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PlayCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => ContainerPercents.Count == 0 && PlayCounts.Count == 0;
}

/// <summary>
/// [issue #129] What the admin page shows about the target cap: the value
/// in the configuration, the value actually applied after clamping, how many
/// distinct targets the enabled badges reference, and the names past the cap.
/// </summary>
public sealed record TargetCapSummary(int Configured, int Cap, int Observed, IReadOnlyList<string> Dropped);

/// <summary>
/// [issue #107] Computes the two targeted metrics against the library.
/// <para>
/// Kept out of LibraryCompletionService on purpose: that file already carries
/// two unrelated jobs (library folders and artist discography), and targets
/// are a third with a different lifecycle.
/// </para>
/// <para>
/// Leaf resolution has two shapes because Jellyfin has two shapes of
/// container. Series, Season and MusicAlbum are real folders, so their leaves
/// come from an AncestorIds query. BoxSet and Playlist are Folder subclasses
/// too, but they hold their members as linked children, which AncestorIds
/// does not reach, so those go through GetLinkedChildren and expand any
/// folder member (a whole series dropped into a collection) through the
/// first path.
/// </para>
/// <para>
/// [issue #129] The per-play path used to ask the library once per target
/// whether the played item sat under it, which is what made the target cap
/// necessary at 50. It now reads the played item's ancestor ids once and
/// tests each hierarchical target against that set. Collections and playlists
/// have no such index, so their member ids are cached per target
/// (<see cref="LinkedTargetMembers"/>), dropped when Jellyfin reports the
/// container changed. The cap stays as the admin's ceiling and is clamped to
/// <see cref="MinTargetCap"/>..<see cref="MaxTargetCap"/>.
/// </para>
/// </summary>
public class TargetProgressService : IDisposable
{
    /// <summary>Bounds for MaxTargetedBadgeTargets, whatever the config file says.</summary>
    public const int MinTargetCap = 1;
    public const int MaxTargetCap = 1000;

    private static readonly BaseItemKind[] LeafKinds =
    {
        BaseItemKind.Movie,
        BaseItemKind.Episode,
        BaseItemKind.Audio,
        BaseItemKind.AudioBook,
        BaseItemKind.Book,
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly CustomBadgeService _customBadges;
    private readonly AchievementBadgeService _badgeService;
    private readonly ILogger<TargetProgressService> _logger;
    private readonly LinkedTargetMembers _linkedMembers = new();
    private bool _disposed;

    public TargetProgressService(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        CustomBadgeService customBadges,
        AchievementBadgeService badgeService,
        ILogger<TargetProgressService> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _customBadges = customBadges;
        _badgeService = badgeService;
        _logger = logger;
        // [issue #129] Adding or removing a member updates the collection or
        // playlist item, which is the signal to drop its cached member set.
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
    }

    /// <summary>
    /// Played leaves over total leaves. Null for an empty container: zero of
    /// zero otherwise reads as complete and hands out the badge for free.
    /// </summary>
    public static int? Percent(int total, int played)
    {
        if (total <= 0)
        {
            return null;
        }

        return (int)Math.Round(100.0 * played / total);
    }

    /// <summary>
    /// Full pass over every observed target. It is the reconciler: the only
    /// path that recomputes targets the live path never sees.
    /// </summary>
    public TargetProgressResult RecomputeForUser(Guid userGuid)
    {
        var result = new TargetProgressResult();
        var user = _userManager.GetUserById(userGuid);
        if (user is null)
        {
            return result;
        }

        var targets = CollectTargets();
        if (targets.Count == 0)
        {
            return result;
        }

        try
        {
            foreach (var target in targets)
            {
                Compute(user, target, result);
            }
        }
        catch (Exception ex)
        {
            // Same guard as the library and artist recomputes: a failed
            // enumeration must not publish a partial map over progress that is
            // still true.
            _logger.LogWarning(ex, "[AchievementBadges] Target recompute failed for user {UserId}", userGuid);
            return new TargetProgressResult();
        }

        Publish(userGuid, result);
        return result;
    }

    /// <summary>
    /// Scoped pass for the live path: only the targets that contain the item
    /// whose played flag just changed.
    /// </summary>
    public TargetProgressResult RecomputeForItem(Guid userGuid, BaseItem item)
    {
        var result = new TargetProgressResult();
        if (item is null)
        {
            return result;
        }

        var user = _userManager.GetUserById(userGuid);
        if (user is null)
        {
            return result;
        }

        var ancestors = AncestorsOf(item);
        foreach (var target in CollectTargets())
        {
            try
            {
                if (!Contains(user, target, item, ancestors))
                {
                    continue;
                }

                Compute(user, target, result);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[AchievementBadges] Scoped target recompute failed for {Target}", target.Name);
            }
        }

        Publish(userGuid, result);
        return result;
    }

    public void RecomputeAll()
    {
        foreach (var user in _userManager.EnumerateAll())
        {
            RecomputeForUser(user.Id);
        }
    }

    private IReadOnlyList<ObservedTarget> CollectTargets()
    {
        var cap = EffectiveCap(Plugin.Instance?.Configuration?.MaxTargetedBadgeTargets);
        var targets = ObservedTargets.Collect(_customBadges.GetEnabled(), cap, out var dropped);
        if (dropped.Count > 0)
        {
            _logger.LogWarning(
                "[AchievementBadges] {Count} badge targets exceed the cap of {Cap} and are not computed: {Names}",
                dropped.Count, cap, string.Join(", ", dropped));
        }

        return targets;
    }

    /// <summary>
    /// Both writers merge. The maps are keyed by target, and a pass that only
    /// looked at one target must not erase the rest: replacing from the scoped
    /// path is the defect fixed for artists in 3c23df4.
    /// </summary>
    private void Publish(Guid userGuid, TargetProgressResult result)
    {
        if (result.IsEmpty)
        {
            return;
        }

        var userId = userGuid.ToString("D");
        _badgeService.MergeContainerCompletionPercents(userId, result.ContainerPercents);
        _badgeService.MergeItemPlayCounts(userId, result.PlayCounts);
    }

    private void Compute(User user, ObservedTarget target, TargetProgressResult result)
    {
        var item = Resolve(user, target);
        if (item is null)
        {
            // Keep the last known value. A collection deleted this morning
            // should not zero everyone's progress bar this afternoon.
            _logger.LogDebug("[AchievementBadges] Target did not resolve: {Name}", target.Name);
            return;
        }

        var key = item.Id.ToString("N");

        if (target.Metric == AchievementMetric.ItemPlayCount)
        {
            var data = _userDataManager.GetUserData(user, item);
            result.PlayCounts[key] = data?.PlayCount ?? 0;
            return;
        }

        if (item is not Folder folder)
        {
            return;
        }

        var leaves = Leaves(user, folder);
        var played = leaves.Count(l => _userDataManager.GetUserData(user, l)?.Played == true);
        var percent = Percent(leaves.Count, played);
        if (percent.HasValue)
        {
            result.ContainerPercents[key] = percent.Value;
        }
    }

    private BaseItem? Resolve(User user, ObservedTarget target)
    {
        if (target.Id != Guid.Empty)
        {
            var byId = _libraryManager.GetItemById(target.Id);
            if (byId is not null)
            {
                return byId;
            }
        }

        // Fallback by name. This is the case a mass rename upstream of
        // Jellyfin produces: the item was deleted and re-added, so the GUID is
        // dead while the title is intact.
        var query = new InternalItemsQuery(user)
        {
            Name = target.Name,
            Recursive = true,
            EnableTotalRecordCount = false,
        };

        var matches = _libraryManager.GetItemsResult(query).Items;
        if (matches.Count != 1)
        {
            // Zero means gone; more than one means ambiguous, and guessing
            // would silently attach the badge to the wrong show.
            return null;
        }

        var found = matches[0];
        RewriteParameter(target, found.Id);
        return found;
    }

    private void RewriteParameter(ObservedTarget target, Guid resolvedId)
    {
        foreach (var badge in _customBadges.GetEnabled())
        {
            if (RewriteNode(badge.Criteria, target, resolvedId))
            {
                _customBadges.Upsert(badge);
                _logger.LogInformation(
                    "[AchievementBadges] Target {Name} re-resolved to {Id}; badge {Badge} updated",
                    target.Name, resolvedId, badge.Name);
            }
        }
    }

    private static bool RewriteNode(CustomBadgeCriteria? node, ObservedTarget target, Guid resolvedId)
    {
        if (node is null)
        {
            return false;
        }

        if (node.Children is { Count: > 0 })
        {
            var changed = false;
            foreach (var child in node.Children)
            {
                changed |= RewriteNode(child, target, resolvedId);
            }

            return changed;
        }

        if (node.Metric != target.Metric
            || !string.Equals(node.MetricParameter, target.Parameter, StringComparison.Ordinal))
        {
            return false;
        }

        node.MetricParameter = TargetRef.Format(resolvedId, target.Name);
        return true;
    }

    private IReadOnlyList<BaseItem> Leaves(User user, Folder folder)
    {
        if (!IsLinkedContainer(folder))
        {
            return Descendants(user, folder.Id);
        }

        var leaves = new List<BaseItem>();
        foreach (var child in folder.GetLinkedChildren(user))
        {
            if (child is Folder inner)
            {
                leaves.AddRange(Descendants(user, inner.Id));
            }
            else
            {
                leaves.Add(child);
            }
        }

        return leaves;
    }

    /// <summary>
    /// Collections and playlists hold members as linked children, which an
    /// AncestorIds query does not reach. Matched on type name rather than on
    /// the concrete types so this file does not take a compile-time reference
    /// to the collections and playlists assemblies for a two-branch decision.
    /// </summary>
    private static bool IsLinkedContainer(Folder folder)
    {
        var typeName = folder.GetType().Name;
        return string.Equals(typeName, "BoxSet", StringComparison.Ordinal)
            || string.Equals(typeName, "Playlist", StringComparison.Ordinal);
    }

    private IReadOnlyList<BaseItem> Descendants(User user, Guid ancestorId)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = LeafKinds,
            AncestorIds = new[] { ancestorId },
            Recursive = true,
            EnableTotalRecordCount = false,
        };

        return _libraryManager.GetItemsResult(query).Items;
    }

    /// <summary>The cap as the admin page reports it, for the enabled badges.</summary>
    public TargetCapSummary Summarize()
    {
        return Summarize(_customBadges.GetEnabled(), Plugin.Instance?.Configuration?.MaxTargetedBadgeTargets);
    }

    /// <summary>Pure form of <see cref="Summarize()"/>: the same walk CollectTargets runs, without the log line.</summary>
    public static TargetCapSummary Summarize(IEnumerable<CustomBadge> badges, int? configured)
    {
        var cap = EffectiveCap(configured);
        var observed = ObservedTargets.Collect(badges, cap, out var dropped);
        return new TargetCapSummary(configured ?? 50, cap, observed.Count, dropped);
    }

    /// <summary>The configured cap, kept inside the bounds the feature was sized for.</summary>
    public static int EffectiveCap(int? configured)
    {
        return Math.Clamp(configured ?? 50, MinTargetCap, MaxTargetCap);
    }

    /// <summary>
    /// [issue #129] Whether a target can be decided without asking the library:
    /// a name-only target always goes to Compute (which resolves it), and an
    /// item target is a plain id comparison. Null means "look at the library".
    /// </summary>
    public static bool? DecideWithoutLibrary(ObservedTarget target, Guid itemId)
    {
        if (target.Id == Guid.Empty)
        {
            return true;
        }

        if (target.Metric == AchievementMetric.ItemPlayCount)
        {
            return target.Id == itemId;
        }

        return null;
    }

    /// <summary>A hierarchical target contains the item when it is one of the item's ancestors.</summary>
    public static bool UnderAncestor(Guid targetId, IReadOnlySet<Guid> ancestors)
    {
        return ancestors.Contains(targetId);
    }

    private HashSet<Guid>? AncestorsOf(BaseItem item)
    {
        try
        {
            return new HashSet<Guid>(item.GetAncestorIds());
        }
        catch (Exception ex)
        {
            // The old per-target query still works; only the shortcut is lost.
            _logger.LogDebug(ex, "[AchievementBadges] Could not read ancestors of {Item}; falling back to library queries", item.Id);
            return null;
        }
    }

    private bool Contains(User user, ObservedTarget target, BaseItem item, IReadOnlySet<Guid>? ancestors)
    {
        var decided = DecideWithoutLibrary(target, item.Id);
        if (decided.HasValue)
        {
            return decided.Value;
        }

        // A target whose id no longer resolves goes to Compute, which re-resolves
        // it by name and rewrites the badge; a resolved non-folder is a no-op there.
        if (_libraryManager.GetItemById(target.Id) is not Folder folder)
        {
            return true;
        }

        if (IsLinkedContainer(folder))
        {
            // User-agnostic on purpose: the set answers membership only, and
            // Compute re-reads the members through the user's own view.
            return _linkedMembers.Contains(folder.Id, item.Id, () => LeafIdsOfLinked(folder));
        }

        if (ancestors is not null)
        {
            return UnderAncestor(target.Id, ancestors);
        }

        return ContainsByQuery(user, target.Id, item.Id);
    }

    /// <summary>Leaf ids of a collection or playlist without a user filter,
    /// folder members expanded the way <see cref="Leaves"/> does.</summary>
    private IEnumerable<Guid> LeafIdsOfLinked(Folder folder)
    {
        foreach (var child in folder.GetLinkedChildren())
        {
            if (child is Folder inner)
            {
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = LeafKinds,
                    AncestorIds = new[] { inner.Id },
                    Recursive = true,
                    EnableTotalRecordCount = false,
                };
                foreach (var leaf in _libraryManager.GetItemsResult(query).Items)
                {
                    yield return leaf.Id;
                }
            }
            else
            {
                yield return child.Id;
            }
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e?.Item is not null && _linkedMembers.Invalidate(e.Item.Id))
        {
            _logger.LogDebug("[AchievementBadges] Dropped cached members of {Id} after a library change", e.Item.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        _linkedMembers.Clear();
        GC.SuppressFinalize(this);
    }

    private bool ContainsByQuery(User user, Guid targetId, Guid itemId)
    {
        var query = new InternalItemsQuery(user)
        {
            AncestorIds = new[] { targetId },
            ItemIds = new[] { itemId },
            Recursive = true,
            EnableTotalRecordCount = false,
        };

        return _libraryManager.GetItemsResult(query).Items.Count > 0;
    }
}
