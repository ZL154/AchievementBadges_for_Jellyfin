using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.AchievementBadges.Api;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Regression tests for issue #155. With a base URL (https://example.com/jellyfin
/// behind a reverse proxy that forwards only that prefix) everything Jellyfin
/// serves lives under {base}/, and a request for /Plugins/... or /Users/...
/// from the server root never reaches Jellyfin. The client files take the
/// folder from the URL the browser resolved for their own script whenever
/// Jellyfin's ApiClient cannot build one, and the pages the plugin renders
/// itself point at the server through <see cref="ServerRoot"/>.
/// </summary>
public class BaseUrlAssetTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(".Pages.enhance.js")]
    [InlineData(".Pages.sidebar.js")]
    [InlineData(".Pages.index.html")]
    [InlineData(".Pages.standalone.js")]
    [InlineData(".Configuration.configPage.html")]
    public void Client_files_ask_for_nothing_at_the_server_root(string file)
    {
        var text = ReadEmbedded(file);

        // A quote or url( straight before /Plugins/ is a URL built from the
        // server root; the Plugin Pages selector (pageUrl=/Plugins/...) is a
        // text match on links, not a request, and does not hit this pattern.
        Assert.DoesNotMatch(new Regex("['\"(]/Plugins/AchievementBadges/"), text);
        // Nor a link or a source written from the server root (/web/...),
        // plainly or inside a JS string.
        Assert.DoesNotMatch(new Regex("(?:href|src)=\\\\?[\"']/(?!/)"), text);
        // Nor a URL built without ApiClient on a bare '/'. A '/' between two
        // values ("3/10") follows a '+', not a return or a ternary.
        Assert.DoesNotMatch(new Regex("(?:return|[?:])\\s*['\"]/['\"]\\s*\\+"), text);
    }

    [Fact]
    public void Back_home_link_starts_at_the_server_root_of_its_own_script()
    {
        var js = ReadEmbedded(".Pages.standalone.js");

        Assert.Contains("'<a class=\"ab-back\" href=\"' + SERVER_ROOT + 'web/index.html#/home\">", js, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".Pages.profile-card.html")]
    [InlineData(".Pages.profile-card-blades.html")]
    [InlineData(".Pages.profile-card-metro.html")]
    public void Profile_card_avatar_and_back_link_resolve_under_the_card_base_url(string template)
    {
        const string RequestPath = "/Plugins/AchievementBadges/users/abc/profile-card";
        var card = new Uri("https://example.com/jellyfin" + RequestPath);
        var html = ReadEmbedded(template)
            .Replace("{{root}}", ServerRoot.RelativeFrom(RequestPath), StringComparison.Ordinal)
            .Replace("{{userId}}", "abc", StringComparison.Ordinal);

        Assert.DoesNotMatch(new Regex("(?:url\\(\\s*['\"]?|(?:href|src)=\")/(?!/)"), html);
        var avatar = Regex.Match(html, "background-image:url\\('([^']+)'\\)").Groups[1].Value;
        Assert.Equal("/jellyfin/Users/abc/Images/Primary", new Uri(card, avatar).AbsolutePath);
        var back = Regex.Match(html, "<a class=\"ab-back-btn\" href=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal("/jellyfin/web/index.html", new Uri(card, back).AbsolutePath);
    }

    [Fact]
    public void Enhance_takes_the_spritesheet_from_its_own_folder()
    {
        var js = ReadEmbedded(".Pages.enhance.js");

        Assert.Contains("var CLIENT_SCRIPT_BASE = SERVER_ROOT + 'Plugins/AchievementBadges/client-script/';", js, StringComparison.Ordinal);
        Assert.Contains("background-image:url(\"' + CLIENT_SCRIPT_BASE + 'spritesheet\")", js, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".Pages.enhance.js")]
    [InlineData(".Pages.sidebar.js")]
    [InlineData(".Pages.standalone.js")]
    public void Injected_scripts_fall_back_to_the_server_root_of_their_own_src(string file)
    {
        var js = ReadEmbedded(file);

        Assert.Contains("var SERVER_ROOT = (function () {", js, StringComparison.Ordinal);
        Assert.Contains("var at = src ? src.indexOf('Plugins/AchievementBadges/client-script/') : -1;", js, StringComparison.Ordinal);
        // Relative to the shell at {base}/web/, where the src could be missing.
        Assert.Contains("return '../';", js, StringComparison.Ordinal);
        // buildUrl falls back to it.
        Assert.Matches(new Regex("SERVER_ROOT\\s*\\+\\s*(?:c|clean)\\b"), js);
    }

    [Theory]
    [InlineData(".Pages.sidebar.js", ": SERVER_ROOT + 'Plugins/AchievementBadges/client-script/styles-revamp';")]
    [InlineData(".Pages.index.html", "base = serverRoot() + 'Plugins/AchievementBadges/client-script/styles-revamp';")]
    public void Revamp_sheet_without_ApiClient_comes_from_the_server_root(string file, string expected)
    {
        Assert.Contains(expected, ReadEmbedded(file), StringComparison.Ordinal);
    }

    // The admin pages only run inside the shell, {base}/web/..., so their
    // server root is the shell's own address up to /web/.
    [Theory]
    [InlineData(".Pages.index.html", ": serverRoot() + path;")]
    [InlineData(".Pages.index.html", "return serverRoot() + cleanPath;")]
    [InlineData(".Configuration.configPage.html", "return this.getServerRoot() + clean;")]
    [InlineData(".Configuration.configPage.html", "return this.getServerRoot() + 'web/';")]
    public void Admin_pages_build_urls_without_ApiClient_from_the_shell_address(string file, string expected)
    {
        var html = ReadEmbedded(file);

        Assert.Contains(expected, html, StringComparison.Ordinal);
        Assert.Contains("lastIndexOf('/web/');", html, StringComparison.Ordinal);
    }

    // Each page is opened through a proxy that publishes Jellyfin under its
    // base URL, through one that publishes it under another prefix, and with
    // no prefix at all. HttpRequest.Path is the part below the base URL.
    [Theory]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/users/abc/profile-card?style=metro", "/Plugins/AchievementBadges/users/abc/profile-card", "https://example.com/jellyfin/")]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/users/abc/profile-card/", "/Plugins/AchievementBadges/users/abc/profile-card/", "https://example.com/jellyfin/")]
    [InlineData("https://example.com/jf/Plugins/AchievementBadges/StandalonePage?token=t", "/Plugins/AchievementBadges/StandalonePage", "https://example.com/jf/")]
    [InlineData("https://example.com/Plugins/AchievementBadges/StandalonePage", "/Plugins/AchievementBadges/StandalonePage", "https://example.com/")]
    [InlineData("https://example.com/jellyfin/page", "/page", "https://example.com/jellyfin/")]
    public void Server_root_resolves_to_the_prefix_the_page_was_opened_under(string pageUrl, string requestPath, string expectedRoot)
    {
        Assert.Equal(expectedRoot, new Uri(new Uri(pageUrl), ServerRoot.RelativeFrom(requestPath)).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/StandalonePage?token=t", "/Plugins/AchievementBadges/StandalonePage")]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/StandalonePage/?token=t", "/Plugins/AchievementBadges/StandalonePage/")]
    public async Task Standalone_page_loads_its_script_from_under_its_own_base_url(string pageUrl, string requestPath)
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/jellyfin";
        context.Request.Path = requestPath;
        var controller = new StandalonePageController
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.GetStandalonePage("token", "user", "server");

        var html = Assert.IsType<ContentResult>(result).Content!;
        var src = Regex.Match(html, "<script src=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal("/jellyfin/Plugins/AchievementBadges/client-script/standalone", new Uri(new Uri(pageUrl), src).AbsolutePath);
    }

    [Theory]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/users/abc/profile-card", "/Plugins/AchievementBadges/users/abc/profile-card")]
    [InlineData("https://example.com/jellyfin/Plugins/AchievementBadges/users/abc/profile-card/", "/Plugins/AchievementBadges/users/abc/profile-card/")]
    public void Profile_card_background_loads_from_under_the_card_base_url(string cardUrl, string requestPath)
    {
        var layer = AchievementBadgesController.ProfileCardBackgroundLayer(ServerRoot.RelativeFrom(requestPath), "bg-aurora");

        var src = Regex.Match(layer, "<video[^>]* src=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal("/jellyfin/Plugins/AchievementBadges/asset/bg-aurora", new Uri(new Uri(cardUrl), src).AbsolutePath);
    }
}
