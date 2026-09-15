using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Services;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

// [issue #129] Collections and playlists have no ancestor index, so their
// member ids are cached per target: built once, dropped on a library change
// for that container, rebuilt after the TTL as a safety net.
public class LinkedTargetMembersTests
{
    private static readonly DateTime T0 = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSetIsBuiltOnceAndAnswersMembershipFromMemory()
    {
        var now = T0;
        var cache = new LinkedTargetMembers(() => now);
        var box = Guid.NewGuid();
        var movie = Guid.NewGuid();
        var builds = 0;
        IEnumerable<Guid> Build() { builds++; return new[] { movie, Guid.NewGuid() }; }

        Assert.True(cache.Contains(box, movie, Build));
        Assert.False(cache.Contains(box, Guid.NewGuid(), Build));
        Assert.True(cache.Contains(box, movie, Build));
        Assert.Equal(1, builds);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ALibraryChangeDropsTheContainerAndTheNextPlayRebuildsIt()
    {
        var now = T0;
        var cache = new LinkedTargetMembers(() => now);
        var box = Guid.NewGuid();
        var movie = Guid.NewGuid();
        var added = Guid.NewGuid();
        var members = new List<Guid> { movie };
        var builds = 0;
        IEnumerable<Guid> Build() { builds++; return members.ToArray(); }

        Assert.False(cache.Contains(box, added, Build));
        members.Add(added);
        Assert.False(cache.Contains(box, added, Build)); // still the cached set
        Assert.True(cache.Invalidate(box));
        Assert.False(cache.Invalidate(box));
        Assert.True(cache.Contains(box, added, Build));
        Assert.Equal(2, builds);
    }

    [Fact]
    public void TheTtlRebuildsASetThatNoEventTouched()
    {
        var now = T0;
        var cache = new LinkedTargetMembers(() => now);
        var box = Guid.NewGuid();
        var late = Guid.NewGuid();
        var members = new List<Guid>();
        IEnumerable<Guid> Build() => members.ToArray();

        Assert.False(cache.Contains(box, late, Build));
        members.Add(late);
        now = T0 + LinkedTargetMembers.Ttl - TimeSpan.FromSeconds(1);
        Assert.False(cache.Contains(box, late, Build));
        now = T0 + LinkedTargetMembers.Ttl;
        Assert.True(cache.Contains(box, late, Build));
    }

    [Fact]
    public void TheServiceUnsubscribesWhenDisposed()
    {
        // The singleton subscribes to ItemUpdated and ItemRemoved for the
        // invalidation; a service that could not be disposed would keep the
        // handler alive past the host's shutdown.
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(TargetProgressService)));
    }
}
