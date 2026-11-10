using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #175, the client half: an envelope in Jellyfin's button row on an
/// item's page opens the Friends drawer with the title attached, and the chat
/// shows a received recommendation as a card. The card asks Jellyfin for the
/// item as the reader, so a reader without access to it never sees its title
/// or poster. The envelope's friends check runs from tryInject's DOM mutation
/// loop, so it has to stay one cached, shared request (#161).
/// </summary>
public class RecommendToFriendClientTests
{
    private static readonly string Js = ReadEmbedded("sidebar.js");

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Between(string js, string from, string to)
    {
        var start = js.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from + " not found");
        var end = js.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, to + " not found after " + from);
        return js.Substring(start, end - start);
    }

    private static string Inject() => Between(Js, "function injectRecommendButtons(){", "function startRecommendation(");

    [Fact]
    public void The_envelope_goes_into_Jellyfins_detail_button_row_from_the_inject_loop()
    {
        Assert.Contains(
            "injectRecommendButtons();",
            Between(Js, "function tryInject(){", "// Listen for live preference changes"),
            StringComparison.Ordinal);
        Assert.Contains("'.itemDetailPage .mainDetailButtons'", Inject(), StringComparison.Ordinal);
        Assert.Contains("row.insertBefore(btn, row.querySelector('.btnMoreCommands'));", Inject(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_item_is_read_from_the_route_when_the_envelope_is_clicked()
    {
        // Jellyfin reuses a details view for the next item it shows, so an id
        // taken when the button was drawn could belong to the previous item.
        Assert.Contains(
            "var id = currentDetailItemId();",
            Between(Inject(), "btn.addEventListener('click'", "});"),
            StringComparison.Ordinal);
        Assert.Contains(
            "/^[0-9a-f]{32}$/",
            Between(Js, "function currentDetailItemId(){", "function refreshRecommendHasFriends(){"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_envelope_waits_for_a_friend_and_its_check_cannot_storm_the_server()
    {
        var inject = Inject();
        Assert.Contains("_recommendHasFriends === true", inject, StringComparison.Ordinal);
        // Off an item's page no friends request is made at all.
        Assert.Contains("if (show) refreshRecommendHasFriends();", inject, StringComparison.Ordinal);
        Assert.True(
            inject.IndexOf("var show = onDetails", StringComparison.Ordinal) < inject.IndexOf("refreshRecommendHasFriends()", StringComparison.Ordinal),
            "the route has to be checked before the friends request");

        var check = Between(Js, "function refreshRecommendHasFriends(){", "function injectRecommendButtons(){");
        Assert.Contains("if (_recommendHasFriendsFetch || now < _recommendRetryAt) return;", check, StringComparison.Ordinal);
        Assert.Contains("now - _recommendHasFriendsAt < RECOMMEND_FRIENDS_TTL_MS", check, StringComparison.Ordinal);
        Assert.Contains("_recommendRetryAt = Date.now() + Math.min(", check, StringComparison.Ordinal);
        // A login or a user switch asks again.
        Assert.Contains("_recommendHasFriendsFor !== uid", check, StringComparison.Ordinal);
    }

    [Fact]
    public void Sending_carries_the_item_and_a_recommendation_needs_no_note()
    {
        var send = Between(Js, "function sendChatMessage(){", "// ── Attachments");
        Assert.Contains("if (!text && !attachmentId && !itemId) return;", send, StringComparison.Ordinal);
        Assert.Contains("ItemId: itemId", send, StringComparison.Ordinal);
        Assert.Contains(
            "_recommendItem",
            Between(Js, "function refreshChatSendEnabled(){", "function threadPreviewText("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_asks_Jellyfin_as_the_reader_and_never_builds_a_poster_from_the_message()
    {
        Assert.Contains(
            "buildUrl('Items/' + encodeURIComponent(itemId) + '?userId=' + encodeURIComponent(uid))",
            Between(Js, "function fetchRecommendedItem(itemId){", "function describeRecommendedItem(item){"),
            StringComparison.Ordinal);

        // A poster URL is built from Jellyfin's answer for this reader only,
        // never from the item id a message carries.
        Assert.DoesNotContain(
            "Images/Primary",
            Between(Js, "function renderChatMessages(msgs){", "// Fullscreen image preview"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Images/Primary",
            Between(Js, "function describeRecommendedItem(item){", "function resolveChatItemCards(root){"),
            StringComparison.Ordinal);

        var fill = Between(Js, "function fillChatItemCard(card, info){", "function refreshChatSendEnabled(){");
        Assert.Contains("if (!info.ok) {", fill, StringComparison.Ordinal);
        Assert.Contains("nameEl.textContent = info.name;", fill, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML = info.", fill, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_the_drawer_drops_a_pending_recommendation()
    {
        Assert.Contains(
            "clearRecommendation();",
            Between(Js, "function close(){", "btn.addEventListener('click', open);"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_thread_list_and_notifications_name_a_recommendation()
    {
        var preview = Between(Js, "function threadPreviewText(t){", "\n    }\n");
        Assert.Contains("'[recommendation]'", preview, StringComparison.Ordinal);
        Assert.Contains("'[image]'", preview, StringComparison.Ordinal);
        Assert.Contains(
            "threadPreviewText(t)",
            Between(Js, "function loadMessagesPane(){", "function openMembersModal(){"),
            StringComparison.Ordinal);
        Assert.Contains("showToast(t.otherUserName || '', threadPreviewText(t), t.otherUserId);", Js, StringComparison.Ordinal);
    }
}
