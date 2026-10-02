using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Follow-up to #154. Genre and studio badges match names loosely: TMDb
/// composite genres such as "Action &amp; Adventure", and studio labels such
/// as "Walt Disney Pictures" for the "Disney" badge. One item often carries
/// several names that match the same badge, and the per-name counters cannot
/// tell them apart once they are added up, so each badge target is credited
/// once per item when the playback is recorded. Profiles from before this
/// have no per-target count yet and start from the per-name history: the sum
/// for genres, where a composite and a plain genre rarely sit on one item,
/// and the largest matching name for studios, whose labels usually do.
/// </summary>
public class StudioGenreOncePerItemTests : IDisposable
{
    private const string UserId = "44444444-4444-4444-4444-444444444444";
    private readonly string _dataDir;
    private readonly Mock<IApplicationPaths> _paths = new();

    public StudioGenreOncePerItemTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "abtest_once_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private AchievementBadgeService NewService(CustomBadgeService? customBadges = null) =>
        new(_paths.Object,
            new Mock<IUserManager>().Object,
            new WebhookNotifier(NullLogger<WebhookNotifier>.Instance),
            new AuditLogService(_paths.Object, NullLogger<AuditLogService>.Instance),
            NullLogger<AchievementBadgeService>.Instance,
            customBadges: customBadges);

    private static void Play(AchievementBadgeService badges, string[]? genres = null, string[]? studios = null, bool episode = false) =>
        badges.RecordPlayback(new PlaybackContext
        {
            UserId = UserId,
            ItemId = Guid.NewGuid().ToString("N"),
            IsMovie = !episode,
            IsEpisode = episode,
            Genres = genres,
            Studios = studios,
            PlayedAt = DateTimeOffset.UtcNow,
            RunTimeTicks = TimeSpan.FromHours(1).Ticks,
        });

    private static int Value(AchievementBadgeService badges, string badgeId) =>
        badges.PeekProfile(UserId)!.Badges.First(b => b.Id == badgeId).CurrentValue;

    // A profile from before the per-target counts: only the per-name totals
    // exist. GetBadgesForUser creates the profile without recording a playback.
    private static void SeedHistory(AchievementBadgeService badges, Action<UserAchievementCounters> fill)
    {
        badges.GetBadgesForUser(UserId);
        fill(badges.PeekProfile(UserId)!.Counters);
    }

    [Fact]
    public void A_movie_with_two_Disney_studios_counts_once()
    {
        using var badges = NewService();
        Play(badges, studios: new[] { "Walt Disney Pictures", "Walt Disney Animation Studios" });

        Assert.Equal(1, Value(badges, "studio-disney"));
    }

    [Fact]
    public void An_item_with_a_plain_and_a_composite_genre_counts_once()
    {
        using var badges = NewService();
        Play(badges, genres: new[] { "Action", "Action & Adventure" });
        Play(badges, genres: new[] { "Sci-Fi & Fantasy", "Science Fiction" });

        Assert.Equal(1, Value(badges, "genre-action"));
        Assert.Equal(1, Value(badges, "genre-scifi"));
        Assert.Equal(1, Value(badges, "genre-fantasy"));
    }

    [Fact]
    public void Each_episode_of_a_series_with_two_HBO_names_counts_once()
    {
        using var badges = NewService();
        Play(badges, studios: new[] { "HBO", "HBO Documentary Films" }, episode: true);
        Play(badges, studios: new[] { "HBO Nordic", "HBO Max" }, episode: true);

        Assert.Equal(2, Value(badges, "studio-hbo"));
    }

    [Fact]
    public void Genre_history_from_before_the_change_is_summed()
    {
        using var badges = NewService();
        SeedHistory(badges, c =>
        {
            c.GenreItemCounts["Action"] = 3;
            c.GenreItemCounts["Action & Adventure"] = 4;
        });

        Play(badges, genres: new[] { "Comedy" });
        Assert.Equal(7, Value(badges, "genre-action"));

        Play(badges, genres: new[] { "Action", "Action & Adventure" });
        Assert.Equal(8, Value(badges, "genre-action"));
    }

    [Fact]
    public void Studio_history_from_before_the_change_takes_the_largest_matching_name()
    {
        using var badges = NewService();
        SeedHistory(badges, c =>
        {
            c.StudioItemCounts["Walt Disney Pictures"] = 5;
            c.StudioItemCounts["Walt Disney Animation Studios"] = 3;
        });

        Play(badges, studios: new[] { "A24" });
        Assert.Equal(5, Value(badges, "studio-disney"));

        Play(badges, studios: new[] { "Walt Disney Pictures", "Walt Disney Animation Studios" });
        Assert.Equal(6, Value(badges, "studio-disney"));
    }

    [Fact]
    public void Exact_name_history_is_never_lower_than_before()
    {
        using var badges = NewService();
        SeedHistory(badges, c =>
        {
            c.GenreItemCounts["Horror"] = 12;
            c.StudioItemCounts["Netflix"] = 9;
        });

        Play(badges, genres: new[] { "Comedy" });

        Assert.Equal(12, Value(badges, "genre-horror"));
        Assert.Equal(9, Value(badges, "studio-netflix"));
    }

    [Fact]
    public void A_custom_badge_target_counts_once_per_item()
    {
        // A genre no built-in badge uses, so the count can only come from the
        // custom badge's criteria.
        var custom = new CustomBadgeService(_paths.Object, NullLogger<CustomBadgeService>.Instance);
        var war = custom.Upsert(new CustomBadge
        {
            Name = "Front Line",
            Criteria = new CustomBadgeCriteria
            {
                Metric = AchievementMetric.GenreItemsWatched,
                MetricParameter = "War",
                Threshold = 10,
            },
        });

        using var badges = NewService(custom);
        Play(badges, genres: new[] { "War", "War & Politics" });

        Assert.Equal(1, Value(badges, war.Id));
    }

    [Fact]
    public void A_target_keeps_counting_while_its_badge_is_disabled()
    {
        var custom = new CustomBadgeService(_paths.Object, NullLogger<CustomBadgeService>.Instance);
        var war = custom.Upsert(new CustomBadge
        {
            Name = "Front Line",
            Criteria = new CustomBadgeCriteria
            {
                Metric = AchievementMetric.GenreItemsWatched,
                MetricParameter = "War",
                Threshold = 10,
            },
        });

        using var badges = NewService(custom);
        Play(badges, genres: new[] { "War" });

        war.Enabled = false;
        custom.Upsert(war);
        Play(badges, genres: new[] { "War", "War & Politics" });

        war.Enabled = true;
        custom.Upsert(war);
        Play(badges, genres: new[] { "Comedy" });

        // Left behind while disabled this would read 1; rebuilt from the
        // per-name totals it would read 3.
        Assert.Equal(2, Value(badges, war.Id));
    }

    [Fact]
    public void A_credited_studio_target_survives_the_studio_name_pruning()
    {
        using var badges = NewService();
        Play(badges, studios: new[] { "Walt Disney Pictures" });

        // Past 200 studio names the per-name counter keeps its top 100, and
        // each of these outranks the single Disney playback.
        for (var i = 0; i < 210; i++)
        {
            Play(badges, studios: new[] { $"Studio {i}" });
            Play(badges, studios: new[] { $"Studio {i}" });
        }

        Assert.DoesNotContain("Walt Disney Pictures", badges.PeekProfile(UserId)!.Counters.StudioItemCounts.Keys);
        Assert.Equal(1, Value(badges, "studio-disney"));
    }

    [Fact]
    public void Per_item_counts_survive_a_restart()
    {
        using (var badges = NewService())
        {
            Play(badges, genres: new[] { "Action", "Action & Adventure" });
        }

        // Dispose flushed the debounced save. Rebuilt from the per-name totals
        // this would be 2.
        using var reloaded = NewService();
        Play(reloaded, genres: new[] { "Comedy" });

        Assert.Equal(1, Value(reloaded, "genre-action"));
    }
}
