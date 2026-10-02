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

public class StudioAndGenreMatchingTests : IDisposable
{
    private readonly string _dataDir;
    private readonly AchievementBadgeService _badges;
    private const string UserId = "22222222-2222-2222-2222-222222222222";

    public StudioAndGenreMatchingTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "abtest_match_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);

        var userManager = new Mock<IUserManager>().Object;
        var webhook = new WebhookNotifier(NullLogger<WebhookNotifier>.Instance);
        var audit = new AuditLogService(paths.Object, NullLogger<AuditLogService>.Instance);

        _badges = new AchievementBadgeService(
            paths.Object, userManager, webhook, audit,
            NullLogger<AchievementBadgeService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ActionHero_ProgressesOn_ActionAndAdventure_CompositeGenre()
    {
        for (var i = 0; i < 30; i++)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-action-{i}",
                IsMovie = true,
                Genres = new[] { "Action & Adventure" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
        }

        var profile = _badges.PeekProfile(UserId);
        Assert.NotNull(profile);

        var badge = profile.Badges.FirstOrDefault(b => b.Id == "genre-action");
        Assert.NotNull(badge);
        Assert.True(badge.Unlocked);
        Assert.Equal(30, badge.CurrentValue);
    }

    [Fact]
    public void SciFiAndFantasy_BothProgressOn_CompositeGenre()
    {
        for (var i = 0; i < 30; i++)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-scifi-{i}",
                IsMovie = true,
                Genres = new[] { "Sci-Fi & Fantasy" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
        }

        var profile = _badges.PeekProfile(UserId);
        Assert.NotNull(profile);

        var scifiBadge = profile.Badges.FirstOrDefault(b => b.Id == "genre-scifi");
        Assert.NotNull(scifiBadge);
        Assert.True(scifiBadge.Unlocked);
        Assert.Equal(30, scifiBadge.CurrentValue);

        var fantasyBadge = profile.Badges.FirstOrDefault(b => b.Id == "genre-fantasy");
        Assert.NotNull(fantasyBadge);
        Assert.True(fantasyBadge.Unlocked);
        Assert.Equal(30, fantasyBadge.CurrentValue);
    }

    [Fact]
    public void HouseOfMouse_UnlocksOn_WaltDisneyVariants()
    {
        // 25 "Walt Disney Pictures" and 25 "Walt Disney Studios"
        for (var i = 0; i < 25; i++)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-wdp-{i}",
                IsMovie = true,
                Studios = new[] { "Walt Disney Pictures" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-wds-{i}",
                IsMovie = true,
                Studios = new[] { "Walt Disney Studios" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(30 + i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
        }

        var profile = _badges.PeekProfile(UserId);
        Assert.NotNull(profile);

        var badge = profile.Badges.FirstOrDefault(b => b.Id == "studio-disney");
        Assert.NotNull(badge);
        Assert.True(badge.Unlocked);
        Assert.Equal(50, badge.CurrentValue);
    }

    [Fact]
    public void StudioBadges_MatchWellKnownAliases_HBO_BBC_A24()
    {
        for (var i = 0; i < 25; i++)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-hbo-{i}",
                IsMovie = true,
                Studios = new[] { "Home Box Office (HBO)" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-bbc-{i}",
                IsMovie = true,
                Studios = new[] { "BBC One" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
        }

        for (var i = 0; i < 15; i++)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = UserId,
                ItemId = $"item-a24-{i}",
                IsMovie = true,
                Studios = new[] { "A24 Films" },
                PlayedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                RunTimeTicks = TimeSpan.FromHours(1).Ticks
            });
        }

        var profile = _badges.PeekProfile(UserId);
        Assert.NotNull(profile);

        var hboBadge = profile.Badges.FirstOrDefault(b => b.Id == "studio-hbo");
        Assert.NotNull(hboBadge);
        Assert.True(hboBadge.Unlocked);
        Assert.Equal(25, hboBadge.CurrentValue);

        var bbcBadge = profile.Badges.FirstOrDefault(b => b.Id == "studio-bbc");
        Assert.NotNull(bbcBadge);
        Assert.True(bbcBadge.Unlocked);
        Assert.Equal(25, bbcBadge.CurrentValue);

        var a24Badge = profile.Badges.FirstOrDefault(b => b.Id == "studio-a24");
        Assert.NotNull(a24Badge);
        Assert.True(a24Badge.Unlocked);
        Assert.Equal(15, a24Badge.CurrentValue);
    }
}
