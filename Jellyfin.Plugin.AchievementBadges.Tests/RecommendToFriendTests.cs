using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AchievementBadges.Api;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #175: a message can carry a library item the sender recommends. The
/// server keeps the item only when Jellyfin shows it to the sender, so the
/// feature cannot reveal anything outside the sender's own libraries and
/// parental rating. Each reader's client then asks Jellyfin for the item with
/// its own session.
/// </summary>
public sealed class RecommendToFriendTests : IDisposable
{
    private const string Sender = "11111111-1111-1111-1111-111111111111";
    private const string Friend = "22222222-2222-2222-2222-222222222222";
    private const string Unknown = "33333333-3333-3333-3333-333333333333";
    private const string NotAvailable = "That title isn't available to recommend.";

    private static readonly Guid VisibleItem = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid HiddenItem = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly string _dataDir;
    private readonly Mock<IApplicationPaths> _paths = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly AchievementBadgeService _badges;
    private readonly FriendsService _friends;
    private readonly MessagingService _messaging;

    public RecommendToFriendTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "abrecommend_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);

        var all = new List<User> { NewUser(Sender, "sender"), NewUser(Friend, "friend") };
        foreach (var user in all)
        {
            _users.Setup(m => m.GetUserById(user.Id)).Returns(user);
        }
#if NET10_0_OR_GREATER
        _users.Setup(m => m.GetUsers()).Returns(all);
#else
        _users.SetupGet(m => m.Users).Returns(all);
#endif

        // Jellyfin's user-scoped lookup returns only what that user may see:
        // the sender sees VisibleItem, and nobody sees HiddenItem.
        _library.Setup(l => l.GetItemById<BaseItem>(VisibleItem, It.Is<User>(u => u.Id == Guid.Parse(Sender))))
            .Returns(new Movie { Id = VisibleItem, Name = "Visible" });

        _badges = new AchievementBadgeService(
            _paths.Object,
            _users.Object,
            new WebhookNotifier(NullLogger<WebhookNotifier>.Instance),
            new AuditLogService(_paths.Object, NullLogger<AuditLogService>.Instance),
            NullLogger<AchievementBadgeService>.Instance);
        _friends = new FriendsService(
            _badges,
            new Mock<ISessionManager>().Object,
            _users.Object,
            _library.Object,
            new Mock<IUserDataManager>().Object);
        _messaging = NewMessaging();
        MakeFriends(Sender, Friend);
    }

    public void Dispose()
    {
        _messaging.Dispose();
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_recommendation_needs_no_note_and_reaches_the_friend_with_its_item()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);

        var (ok, error, sent) = _messaging.SendToConversation(
            Sender, "sender", conv.Id, string.Empty, null, VisibleItem.ToString("D"));

        Assert.True(ok, error);
        Assert.Equal(VisibleItem.ToString("N"), sent!.ItemId);
        var received = Assert.Single(_messaging.GetConversationMessages(Friend, conv.Id));
        Assert.Equal(VisibleItem.ToString("N"), received.ItemId);
        Assert.Equal(string.Empty, received.Text);
    }

    [Fact]
    public void An_item_the_sender_cannot_see_is_refused_and_nothing_is_stored()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);

        var (ok, error, sent) = _messaging.SendToConversation(
            Sender, "sender", conv.Id, "watch this", null, HiddenItem.ToString("D"));

        Assert.False(ok);
        Assert.Equal(NotAvailable, error);
        Assert.Null(sent);
        Assert.Empty(_messaging.GetConversationMessages(Friend, conv.Id));
    }

    [Fact]
    public void A_sender_Jellyfin_does_not_know_is_refused_without_an_unscoped_lookup()
    {
        // Jellyfin's GetItemById(id, Guid userId) passes a null user on when
        // the id matches no account, and a null user skips the access check.
        // Every lookup that is not scoped to a resolved user would find the
        // item here, so the only way to refuse is to never make one.
        var item = new Movie { Id = VisibleItem };
        _library.Setup(l => l.GetItemById(VisibleItem)).Returns(item);
        _library.Setup(l => l.GetItemById<BaseItem>(VisibleItem)).Returns(item);
        _library.Setup(l => l.GetItemById<BaseItem>(VisibleItem, It.IsAny<Guid>())).Returns(item);
        _library.Setup(l => l.GetItemById<BaseItem>(VisibleItem, (User?)null)).Returns(item);

        Assert.Null(MessagingService.ResolveRecommendedItem(_users.Object, _library.Object, Unknown, VisibleItem.ToString("D")));
        _library.Verify(l => l.GetItemById(It.IsAny<Guid>()), Times.Never);
        _library.Verify(l => l.GetItemById<BaseItem>(It.IsAny<Guid>()), Times.Never);
        _library.Verify(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _library.Verify(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), (User?)null), Times.Never);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaa-0000-0000-0000-00000000000")]
    public void Malformed_item_ids_are_refused(string raw)
    {
        Assert.Null(MessagingService.ResolveRecommendedItem(_users.Object, _library.Object, Sender, raw));
        _library.Verify(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<User?>()), Times.Never);
    }

    [Fact]
    public void The_item_id_is_kept_in_its_32_digit_form_whatever_form_it_came_in()
    {
        Assert.Equal(
            VisibleItem.ToString("N"),
            MessagingService.ResolveRecommendedItem(_users.Object, _library.Object, Sender, VisibleItem.ToString("N").ToUpperInvariant()));
        Assert.Equal(
            VisibleItem.ToString("N"),
            MessagingService.ResolveRecommendedItem(_users.Object, _library.Object, Sender, " " + VisibleItem.ToString("D") + " "));
    }

    [Fact]
    public void A_whitespace_item_id_means_no_item_so_an_empty_message_stays_empty()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);

        var (ok, error, _) = _messaging.SendToConversation(Sender, "sender", conv.Id, string.Empty, null, "   ");

        Assert.False(ok);
        Assert.Equal("Message is empty.", error);
    }

    [Fact]
    public void The_thread_list_flags_a_recommendation_sent_without_a_note()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);
        _messaging.SendToConversation(Sender, "sender", conv.Id, string.Empty, null, VisibleItem.ToString("D"));

        var thread = Assert.Single(_messaging.GetThreads(Friend));
        Assert.True(thread.HasItem);
        Assert.Equal("[recommendation]", thread.LastMessage);
    }

    [Fact]
    public void The_note_stays_the_thread_preview_when_there_is_one()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);
        _messaging.SendToConversation(Sender, "sender", conv.Id, "watch this", null, VisibleItem.ToString("D"));

        var thread = Assert.Single(_messaging.GetThreads(Friend));
        Assert.True(thread.HasItem);
        Assert.Equal("watch this", thread.LastMessage);
    }

    [Fact]
    public void The_item_survives_a_restart()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);
        _messaging.SendToConversation(Sender, "sender", conv.Id, string.Empty, null, VisibleItem.ToString("D"));
        _messaging.FlushPendingSave();

        using var reloaded = NewMessaging();
        var message = Assert.Single(reloaded.GetConversationMessages(Friend, conv.Id));
        Assert.Equal(VisibleItem.ToString("N"), message.ItemId);
    }

    [Fact]
    public void Both_send_routes_pass_the_item_on()
    {
        var conv = _messaging.GetOrCreateDm(Sender, Friend);
        var controller = ControllerAs(Sender);

        controller.SendMessage(Sender, Friend, new AchievementBadgesController.SendMessageRequest { ItemId = VisibleItem.ToString("D") });
        controller.SendConvMessage(Sender, conv.Id, new AchievementBadgesController.SendMessageRequest { ItemId = VisibleItem.ToString("D") });

        var stored = _messaging.GetConversationMessages(Friend, conv.Id);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, m => Assert.Equal(VisibleItem.ToString("N"), m.ItemId));
    }

    private MessagingService NewMessaging()
        => new(_paths.Object, _friends, _badges, _users.Object, _library.Object, NullLogger<MessagingService>.Instance);

    private AchievementBadgesController ControllerAs(string userId)
    {
        var controller = new AchievementBadgesController(
            _badges, null!, null!, null!, null!, null!, null!, null!, _users.Object, null!, _friends, _messaging, null!, null!, null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", N(userId)) }, "test"))
            }
        };
        return controller;
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

    private static User NewUser(string id, string name) => new(name, "prov", "reset") { Id = Guid.Parse(id) };

    private static string N(string id) => Guid.Parse(id).ToString("N");
}
