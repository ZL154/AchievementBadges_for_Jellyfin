using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// #161 and its follow-up: browsers behind nginx and fail2ban sent hundreds of
/// requests per second to public-config and preferences. The page could load
/// the plugin scripts twice, because the middleware only looked for the
/// bootstrap in the last 4 KB while other plugins append their scripts after
/// the on-disk block. And sidebar.js turned every failed showcase check into
/// DOM churn that triggered the next check 250ms later, so a downed server got
/// a new pair from every open tab about four times a second.
/// </summary>
public class RequestStormTests
{
    private const string BlockStart = "<!-- achievementbadges-bootstrap -->";

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

    [Theory]
    [InlineData(null)]
    [InlineData("gzip")]
    public async Task APageWhoseBootstrapSitsFarAboveTheBodyEndIsLeftAlone(string? contentEncoding)
    {
        // Custom Tabs alone inlines about 22 KB before </body>, after our block.
        var page = "<html><body><div>content</div>" + BlockStart +
            "<script src=\"client-script/sidebar\"></script>" +
            "<!-- /achievementbadges-bootstrap -->" +
            "<script>var anotherPlugin = '" + new string('x', 8192) + "';</script>" +
            "</body></html>";
        var payload = Encoding.UTF8.GetBytes(page);
        if (contentEncoding != null)
        {
            payload = ResponseCompression.Compress(payload, contentEncoding);
        }

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/web/index.html";
        var sink = new MemoryStream();
        context.Response.Body = sink;

        var middleware = new SidebarInjectionMiddleware(
            async ctx =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                if (contentEncoding != null)
                {
                    ctx.Response.Headers["Content-Encoding"] = contentEncoding;
                }

                await ctx.Response.Body.WriteAsync(payload);
            },
            NullLogger<SidebarInjectionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        // The same bytes, so still one bootstrap and the validators Jellyfin
        // sent still describe the body.
        Assert.Equal(payload, sink.ToArray());
    }

    [Theory]
    [InlineData("sidebar.js", "__abSidebarLoaded")]
    [InlineData("standalone.js", "__abStandaloneLoaded")]
    [InlineData("enhance.js", "__abEnhanceLoaded")]
    public void EachScriptReturnsBeforeAnythingElseWhenLoadedTwice(string asset, string flag)
    {
        // A second copy must leave before it starts a timer, an observer or
        // a request, so the guard is the first statement of the IIFE.
        var js = ReadEmbedded(asset);
        Assert.StartsWith("(function", js, StringComparison.Ordinal);
        var statements = js.Substring(js.IndexOf('{', StringComparison.Ordinal) + 1).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        Assert.Equal("if (window." + flag + ") return;", statements[0]);
        Assert.Equal("window." + flag + " = true;", statements[1]);
    }

    [Fact]
    public void AFailedShowcaseCheckKeepsTheLastAnswerAndWaits()
    {
        var js = ReadEmbedded("sidebar.js");

        // Only the declaration sets the flag to null. Re-checks used to null
        // it first, so every failure became "unknown", and tryInject removed
        // and re-created the header strip for it, which triggered the next
        // check.
        Assert.Single(Regex.Matches(js, @"_showcaseEnabled\s*=\s*null"));
        Assert.Contains("var _showcaseEnabled = null;", js, StringComparison.Ordinal);

        var failed = Between(js, "if (!anySucceeded) {", "_showcaseFailures = 0;");
        Assert.Contains("_showcaseRetryAt = performance.now()", failed, StringComparison.Ordinal);
        Assert.Contains("return _showcaseEnabled;", failed, StringComparison.Ordinal);
        Assert.Contains(
            "if (performance.now() < _showcaseRetryAt) return Promise.resolve(_showcaseEnabled);",
            Between(js, "function refreshShowcaseEnabled(){", "function fetchShowcaseJson("),
            StringComparison.Ordinal);

        // Unknown is not "disabled", and nothing is injected for it.
        Assert.Contains(
            "if (enabled === false) removeShowcaseDom();",
            Between(js, "function tryInject(){", "injectSidebar();"),
            StringComparison.Ordinal);
        Assert.Contains(
            "if (_showcaseEnabled !== true) return;",
            Between(js, "function injectHeader(){", "legacyRight"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShowcaseRequestsGiveUpAndBelongToTheSignedInUser()
    {
        var js = ReadEmbedded("sidebar.js");

        // One request that never answered held every caller of the shared
        // one, injection included.
        var fetcher = Between(js, "function fetchShowcaseJson(", "function _resolveShowcaseEnabledNow(");
        Assert.Contains("SHOWCASE_FETCH_TIMEOUT_MS", fetcher, StringComparison.Ordinal);
        Assert.Contains("Promise.race([answer, gaveUp])", fetcher, StringComparison.Ordinal);
        var resolve = Between(js, "function _resolveShowcaseEnabledNow(", "function removeShowcaseDom(");
        Assert.DoesNotContain("fetch(buildUrl(", resolve, StringComparison.Ordinal);
        Assert.Contains("fetchShowcaseJson('Plugins/AchievementBadges/public-config'", resolve, StringComparison.Ordinal);

        // A login or a user switch asks again instead of keeping the
        // previous user's answer.
        Assert.Contains(
            "_showcaseFor === getUserId()",
            Between(js, "function resolveShowcaseEnabled(){", "function refreshShowcaseEnabled(){"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BothFriendsMountPathsStartTheUnreadPoll()
    {
        var js = ReadEmbedded("sidebar.js");

        Assert.Contains(
            "_chatUnreadPollTimer = setInterval(refreshUnreadCounts, 20000);",
            Between(js, "function mountFriends(cfg){", "function ensureFriendsDrawer(){"),
            StringComparison.Ordinal);
        Assert.Contains(
            "mountFriends(cfg);",
            Between(js, "function ensureFriendsDrawer(){", "var _friendsAdminEnabled = true;"),
            StringComparison.Ordinal);

        // With Friends off when the page loaded, the 15s reconcile is the only
        // path that mounts once the admin turns it back on.
        var reconcile = Between(js, "var _friendsAdminEnabled = true;", "}, 15000);");
        Assert.Contains("mountFriends(cfg);", reconcile, StringComparison.Ordinal);
        Assert.DoesNotContain("_actuallyMount();", reconcile, StringComparison.Ordinal);

        Assert.Contains(
            "clearInterval(_chatUnreadPollTimer); _chatUnreadPollTimer = null;",
            Between(js, "function _destroyAllFriendsDom(){", "function _actuallyMount(){"),
            StringComparison.Ordinal);
    }
}
