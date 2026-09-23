using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.AchievementBadges.Configuration;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #136: a top-center placement for unlock toasts, and an admin default
/// for users who have not picked one. The second half hinges on telling "never
/// chose" apart from "chose top-right", which the old model could not do
/// because it stored top-right for everyone.
/// </summary>
public class ToastPlacementTests : IDisposable
{
    private readonly string _dataDir;

    public ToastPlacementTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "abtoast_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string Legacy = "11111111-1111-1111-1111-111111111111";
    private const string ChoseCorner = "22222222-2222-2222-2222-222222222222";
    private const string ChoseTopRightAfterUpgrade = "33333333-3333-3333-3333-333333333333";

    /// <summary>Writes a badges.json as an older release would have left it,
    /// then builds the service, which loads and migrates it.</summary>
    private AchievementBadgeService ServiceOver(Dictionary<string, UserAchievementProfile> profiles)
    {
        var pluginData = Path.Combine(_dataDir, "achievementbadges");
        Directory.CreateDirectory(pluginData);
        var store = new UserBadgeStore { UserProfiles = profiles };
        File.WriteAllText(Path.Combine(pluginData, "badges.json"), JsonSerializer.Serialize(store));

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);
        var audit = new AuditLogService(paths.Object, NullLogger<AuditLogService>.Instance);
        return new AchievementBadgeService(
            paths.Object, new Mock<IUserManager>().Object,
            new WebhookNotifier(NullLogger<WebhookNotifier>.Instance), audit,
            NullLogger<AchievementBadgeService>.Instance);
    }

    private static UserAchievementProfile Profile(string id, string toastPosition, bool? migrated) => new()
    {
        UserId = id,
        Preferences = new UserNotificationPreferences
        {
            ToastPosition = toastPosition,
            ToastPositionDefaultMigrated = migrated,
        },
    };

    [Theory]
    [InlineData("top-center", "top-center")]
    [InlineData("TOP-CENTER", "top-center")]
    [InlineData(" bottom-left ", "bottom-left")]
    [InlineData("top-right", "top-right")]
    [InlineData("", "top-right")]
    [InlineData(null, "top-right")]
    [InlineData("middle", "top-right")]
    public void The_admin_default_is_always_a_real_placement(string? stored, string expected)
    {
        // An upgraded or hand edited config must keep toasts where they have
        // always been rather than nowhere.
        Assert.Equal(expected, ToastPlacement.NormalizeDefault(stored));
    }

    [Theory]
    [InlineData("top-center", "top-center")]
    [InlineData("Bottom-Center", "bottom-center")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("middle", "")]
    public void A_user_value_is_a_placement_or_nothing(string? sent, string expected)
    {
        // Nothing means "follow the admin", so an unknown value lands where
        // the admin chose, not in a corner nobody picked.
        Assert.Equal(expected, ToastPlacement.NormalizeUserChoice(sent));
    }

    [Fact]
    public void Nothing_moves_on_upgrade_until_the_admin_changes_the_default()
    {
        Assert.Equal("top-right", new PluginConfiguration().DefaultToastPosition);
        Assert.Equal(ToastPlacement.ServerDefault, new UserNotificationPreferences().ToastPosition);
    }

    [Fact]
    public void An_old_top_right_is_read_as_no_choice_once()
    {
        var service = ServiceOver(new Dictionary<string, UserAchievementProfile>
        {
            [Legacy] = Profile(Legacy, "top-right", migrated: null),
            [ChoseCorner] = Profile(ChoseCorner, "bottom-left", migrated: null),
            [ChoseTopRightAfterUpgrade] = Profile(ChoseTopRightAfterUpgrade, "top-right", migrated: true),
        });

        // The old default everyone had: now follows the admin.
        var legacy = service.GetUserPreferences(Legacy);
        Assert.Equal(ToastPlacement.ServerDefault, legacy.ToastPosition);
        Assert.True(legacy.ToastPositionDefaultMigrated);

        // A placement that was never a default was a real choice: kept.
        Assert.Equal("bottom-left", service.GetUserPreferences(ChoseCorner).ToastPosition);

        // Top-right picked after the migration ran is a choice too: kept.
        Assert.Equal("top-right", service.GetUserPreferences(ChoseTopRightAfterUpgrade).ToastPosition);
    }

    [Theory]
    [InlineData("TOP-CENTER", "top-center")]
    [InlineData("middle", "")]
    [InlineData("", "")]
    public void The_preferences_endpoint_stores_a_placement_or_nothing(string sent, string stored)
    {
        // Through the real endpoint, so the allowlist is what is actually
        // applied rather than what the helper would do in isolation.
        var service = ServiceOver(new Dictionary<string, UserAchievementProfile>());
        var controller = new Jellyfin.Plugin.AchievementBadges.Api.AchievementBadgesController(
            service, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!);

        controller.SaveUserPreferences(Legacy, new UserNotificationPreferences { ToastPosition = sent });

        Assert.Equal(stored, service.GetUserPreferences(Legacy).ToastPosition);
    }

    [Fact]
    public void Saving_preferences_marks_the_choice_as_made()
    {
        var service = ServiceOver(new Dictionary<string, UserAchievementProfile>());
        service.SaveUserPreferences(Legacy, new UserNotificationPreferences { ToastPosition = "top-right" });

        var saved = service.GetUserPreferences(Legacy);
        Assert.Equal("top-right", saved.ToastPosition);
        Assert.True(saved.ToastPositionDefaultMigrated);
    }
}
