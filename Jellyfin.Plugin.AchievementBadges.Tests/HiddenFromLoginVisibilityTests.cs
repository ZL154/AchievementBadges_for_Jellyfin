using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.AchievementBadges.Api;
using Jellyfin.Plugin.AchievementBadges.Configuration;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #138: with HideUsersHiddenFromLogin on, an account hidden from
/// Jellyfin's login screen stays out of what other users see here, except for
/// the account itself, administrators, other hidden accounts and the friends
/// it already has. With the option off nothing changes, which is what a
/// server that hides every account from the login screen relies on.
/// </summary>
public class HiddenFromLoginVisibilityTests : IDisposable
{
    private const string Regular = "11111111-1111-1111-1111-111111111111";
    private const string Hidden = "22222222-2222-2222-2222-222222222222";
    private const string OtherHidden = "33333333-3333-3333-3333-333333333333";
    private const string Admin = "44444444-4444-4444-4444-444444444444";
    private const string Friend = "55555555-5555-5555-5555-555555555555";

    private static readonly string[] Everyone = { Regular, Hidden, OtherHidden, Admin, Friend };

    private readonly string _dataDir;
    private readonly Mock<IUserManager> _users = new();
    private readonly AchievementBadgeService _badges;
    private bool _optionOn = true;

    public HiddenFromLoginVisibilityTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "abhidden_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);

        var all = new List<User>
        {
            NewUser(Regular, "regular", hidden: false, admin: false),
            NewUser(Hidden, "hidden", hidden: true, admin: false),
            NewUser(OtherHidden, "otherhidden", hidden: true, admin: false),
            NewUser(Admin, "admin", hidden: false, admin: true),
            NewUser(Friend, "friend", hidden: false, admin: false),
        };
        foreach (var user in all)
        {
            _users.Setup(m => m.GetUserById(user.Id)).Returns(user);
        }
        // Jellyfin 10.11.9 replaced IUserManager.Users with GetUsers(), which is
        // why the plugin reaches the list by reflection; each test target
        // compiles against one of the two shapes.
#if NET10_0_OR_GREATER
        _users.Setup(m => m.GetUsers()).Returns(all);
#else
        _users.SetupGet(m => m.Users).Returns(all);
#endif

        _badges = new AchievementBadgeService(
            paths.Object,
            _users.Object,
            new WebhookNotifier(NullLogger<WebhookNotifier>.Instance),
            new AuditLogService(paths.Object, NullLogger<AuditLogService>.Instance),
            NullLogger<AchievementBadgeService>.Instance,
            hideUsersHiddenFromLogin: () => _optionOn);

        foreach (var id in Everyone)
        {
            _badges.RecordPlayback(new PlaybackContext
            {
                UserId = id,
                ItemId = Guid.NewGuid().ToString("D"),
                IsMovie = true,
                Silent = true
            });
            var profile = _badges.GetOrCreateProfileDirect(id);
            profile.LifetimeScore = 100;
            _badges.SaveProfileDirect(profile);
        }

        // The hidden account and Friend accepted each other before the option.
        MakeFriends(Hidden, Friend);

        // Something to show in the hidden account's equipped preview.
        var hidden = _badges.GetOrCreateProfileDirect(Hidden);
        hidden.EquippedBadgeIds.Add(hidden.Badges.First(b => b.Unlocked).Id);
        _badges.SaveProfileDirect(hidden);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false, true, false, false, false, false, true)]
    [InlineData(true, false, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, false, false)]
    [InlineData(true, true, true, false, false, false, true)]
    [InlineData(true, true, false, true, false, false, true)]
    [InlineData(true, true, false, false, true, false, true)]
    [InlineData(true, true, false, false, false, true, true)]
    public void Rule_HidesAHiddenAccountOnlyFromStrangers(
        bool optionOn, bool targetHidden, bool isSelf, bool viewerIsAdministrator, bool viewerHidden, bool explicitFriends, bool expected)
    {
        Assert.Equal(expected, AchievementBadgeService.IsVisibleToViewer(
            optionOn, targetHidden, isSelf, viewerIsAdministrator, viewerHidden, explicitFriends));
    }

    [Fact]
    public void TheOptionIsOffByDefault()
    {
        Assert.False(new PluginConfiguration().HideUsersHiddenFromLogin);
    }

    [Fact]
    public void WithTheOptionOff_EveryoneStaysVisible()
    {
        _optionOn = false;

        Assert.Equal(Everyone.OrderBy(x => x), Ids(_badges.GetLeaderboard(50, Regular)).OrderBy(x => x));
        Assert.Equal(5, DirectoryIds(Regular).Count);
        Assert.NotNull(_badges.GetPublicProfileSummary(Hidden, Regular));
    }

    [Fact]
    public void Leaderboards_LeaveHiddenAccountsOut_ForARegularUser()
    {
        var expected = new[] { Regular, Admin, Friend }.OrderBy(x => x);

        Assert.Equal(expected, Ids(_badges.GetLeaderboard(50, Regular)).OrderBy(x => x));
        Assert.Equal(expected, Ids(_badges.GetLeaderboardByCategory("movies", 50, Regular)).OrderBy(x => x));
        Assert.Equal(expected, Ids(_badges.GetPrestigeLeaderboard(50, Regular)).OrderBy(x => x));
    }

    [Fact]
    public void HiddenAccounts_Administrators_AndExistingFriends_StillSeeTheHiddenAccount()
    {
        Assert.Contains(Hidden, Ids(_badges.GetLeaderboard(50, Hidden)));
        Assert.Contains(Hidden, Ids(_badges.GetLeaderboard(50, OtherHidden)));
        Assert.Contains(Hidden, Ids(_badges.GetLeaderboard(50, Admin)));

        var friendView = Ids(_badges.GetLeaderboard(50, Friend));
        Assert.Contains(Hidden, friendView);
        Assert.DoesNotContain(OtherHidden, friendView);
    }

    [Fact]
    public void AnUnknownViewer_SeesNoHiddenAccount()
    {
        Assert.DoesNotContain(Hidden, Ids(_badges.GetLeaderboard(50)));
        Assert.False(_badges.CanSee(null, Hidden));
        Assert.True(_badges.CanSee(null, Regular));
    }

    [Fact]
    public void ActivityFeed_LeavesHiddenAccountsOut_ForARegularUser()
    {
        var regularView = FeedUserIds(Regular);
        Assert.Contains(Regular, regularView);
        Assert.DoesNotContain(Hidden, regularView);
        Assert.DoesNotContain(OtherHidden, regularView);

        Assert.Contains(Hidden, FeedUserIds(OtherHidden));
    }

    [Fact]
    public void PublicProfile_AndCompare_TreatAHiddenAccountAsUnknown()
    {
        Assert.Null(_badges.GetPublicProfileSummary(Hidden, Regular));
        Assert.NotNull(_badges.GetPublicProfileSummary(Hidden, Friend));

        Assert.Empty(_badges.GetPublicEquippedPreview(Hidden, Regular));
        Assert.NotEmpty(_badges.GetPublicEquippedPreview(Hidden, Friend));

        Assert.Equal("One or both users not found.", ErrorOf(_badges.CompareUsers(Regular, Hidden, Regular)));
        Assert.Null(ErrorOf(_badges.CompareUsers(Friend, Hidden, Friend)));
    }

    [Fact]
    public void Directory_ListsOnlyWhoTheViewerMaySee_InTheShapeOfJellyfinUsers()
    {
        Assert.Equal(
            new[] { Admin, Friend, Regular }.Select(N).OrderBy(x => x),
            DirectoryIds(Regular).OrderBy(x => x));
        Assert.Equal(5, DirectoryIds(Admin).Count);

        var first = _badges.GetUserDirectory(Regular).First();
        var names = first.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(new[] { "Id", "Name" }, names);
    }

    [Fact]
    public void FriendRequest_ToAHiddenAccount_ReadsAsUserNotFound()
    {
        var friends = new FriendsService(
            _badges,
            new Mock<ISessionManager>().Object,
            _users.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IUserDataManager>().Object);

        Assert.Equal((false, "User not found."), friends.SendRequest(Regular, Hidden));
        Assert.True(friends.SendRequest(OtherHidden, Hidden).ok);
    }

    [Fact]
    public void Routes_JudgeBySignedInUser()
    {
        Assert.DoesNotContain(Hidden, Ids(Value(ControllerAs(Regular).GetLeaderboard(50))));
        Assert.Contains(Hidden, Ids(Value(ControllerAs(OtherHidden).GetLeaderboard(50))));
        Assert.DoesNotContain(Hidden, Ids(Value(ControllerAs(Regular).GetPrestigeLeaderboard(50))));
        Assert.DoesNotContain(Hidden, Ids(Value(ControllerAs(Regular).GetCategoryLeaderboard("movies", 50))));
        Assert.IsType<NotFoundResult>(ControllerAs(Regular).GetPublicProfileSummary(Hidden));
        Assert.IsType<OkObjectResult>(ControllerAs(Friend).GetPublicProfileSummary(Hidden));

        // {userId} puts the directory under the UserOwnershipFilter.
        var route = typeof(AchievementBadgesController)
            .GetMethod(nameof(AchievementBadgesController.GetUserDirectory))!
            .GetCustomAttribute<HttpGetAttribute>()!;
        Assert.Equal("users/{userId}/directory", route.Template);
    }

    [Theory]
    [InlineData("sidebar.js")]
    [InlineData("standalone.js")]
    public void Clients_FindUsersThroughTheDirectory_NotJellyfinsUserList(string script)
    {
        // Jellyfin's /Users lists hidden accounts to any signed-in user, so a
        // client still reading it would put them back in the friend search and
        // the compare picker whatever the server filters.
        var js = EmbeddedPage(script);
        Assert.Contains("'/directory'", js, StringComparison.Ordinal);
        Assert.DoesNotContain("buildUrl('Users')", js, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPage_HasTheOption_AndLoadsAndSavesIt()
    {
        var html = EmbeddedPage("index.html");
        Assert.Contains("id=\"abFcHideUsersHiddenFromLogin\"", html, StringComparison.Ordinal);
        Assert.Contains("hideLoginHiddenEl.checked = !!c.HideUsersHiddenFromLogin;", html, StringComparison.Ordinal);
        Assert.Contains(
            "HideUsersHiddenFromLogin: !!(document.getElementById('abFcHideUsersHiddenFromLogin') || {}).checked,",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureConfig_TreatsAnOmittedOptionAsLeaveItAlone()
    {
        var property = typeof(AchievementBadgesController.FeatureConfigRequest)
            .GetProperty(nameof(AchievementBadgesController.FeatureConfigRequest.HideUsersHiddenFromLogin))!;
        Assert.Equal(typeof(bool?), property.PropertyType);
        Assert.Null(new AchievementBadgesController.FeatureConfigRequest().HideUsersHiddenFromLogin);
    }

    private static User NewUser(string id, string name, bool hidden, bool admin)
    {
        var user = new User(name, "prov", "reset") { Id = Guid.Parse(id) };
        user.SetPermission(PermissionKind.IsHidden, hidden);
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        return user;
    }

    private void MakeFriends(string a, string b)
    {
        var pa = _badges.GetOrCreateProfileDirect(a);
        pa.Friends.Add(N(b));
        _badges.SaveProfileDirect(pa);
        var pb = _badges.GetOrCreateProfileDirect(b);
        pb.Friends.Add(N(a));
        _badges.SaveProfileDirect(pb);
    }

    private AchievementBadgesController ControllerAs(string userId)
    {
        var controller = new AchievementBadgesController(
            _badges, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", N(userId)) }, "test"))
            }
        };
        return controller;
    }

    private List<string> FeedUserIds(string viewer)
    {
        var feed = _badges.GetActivityFeed(1, 100, null, viewer);
        var entries = (IEnumerable)feed.GetType().GetProperty("Entries")!.GetValue(feed)!;
        return Ids(entries);
    }

    private List<string> DirectoryIds(string viewer)
        => _badges.GetUserDirectory(viewer).Select(e => (string)e.GetType().GetProperty("Id")!.GetValue(e)!).ToList();

    private static object Value(ActionResult result) => Assert.IsType<OkObjectResult>(result).Value!;

    private static List<string> Ids(object list)
        => ((IEnumerable)list).Cast<object>()
            .Select(e => Guid.Parse((string)e.GetType().GetProperty("UserId")!.GetValue(e)!).ToString("D"))
            .ToList();

    private static string? ErrorOf(object result)
        => result.GetType().GetProperty("Error")?.GetValue(result) as string;

    private static string N(string id) => Guid.Parse(id).ToString("N");

    private static string EmbeddedPage(string fileName)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
