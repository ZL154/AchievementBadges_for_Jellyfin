using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Jellyfin 12 ships a new default layout ("modern") that keeps the old
/// .mainDrawer and .skinHeader in the DOM only so legacy scripts do not
/// throw; both are hidden. The plugin's sidebar entry and equipped-badge
/// strip were injected into exactly those two elements, so on 12 they
/// existed and nobody could see them, and the only way to the achievements
/// page was gone. These pin the two new hooks: the avatar menu (an MUI Menu
/// with a stable id that stays mounted while closed) and the MUI toolbar.
/// </summary>
public class Jellyfin12ShellTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        // The assertions below pin literal newlines inside CSS blocks. A
        // Windows checkout embeds the asset with CRLF, so normalise here
        // rather than letting the line-ending policy of the clone decide
        // whether the suite passes.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void TheAvatarMenuGetsAnAchievementsEntryBelowProfile()
    {
        var js = ReadEmbedded("sidebar.js");

        Assert.Contains("getElementById('app-user-menu')", js, StringComparison.Ordinal);
        Assert.Contains("a[href*=\"/userprofile\"]", js, StringComparison.Ordinal);
        Assert.Contains("item.setAttribute('href','#/achievements')", js, StringComparison.Ordinal);

        // Placement: right after the Profile item, never appended blindly
        // while Profile exists.
        var inject = js.IndexOf("function injectUserMenu()", StringComparison.Ordinal);
        var afterProfile = js.IndexOf("list.insertBefore(item, profile.nextSibling)", inject, StringComparison.Ordinal);
        Assert.True(inject >= 0);
        Assert.True(afterProfile > inject);

        // The clone has no React handler, so the script closes the menu itself.
        Assert.Contains("#app-user-menu .MuiBackdrop-root", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEntryIsInjectedOnEveryPassAndTranslated()
    {
        var js = ReadEmbedded("sidebar.js");

        var tryInject = js.IndexOf("function tryInject()", StringComparison.Ordinal);
        var header = js.IndexOf("injectHeader();", tryInject, StringComparison.Ordinal);
        var userMenu = js.IndexOf("injectUserMenu();", header, StringComparison.Ordinal);
        Assert.True(tryInject >= 0);
        Assert.True(header > tryInject);
        Assert.True(userMenu > header);

        var translate = js.IndexOf("function applyTranslations()", StringComparison.Ordinal);
        var entryText = js.IndexOf("getElementById(USER_MENU_ID)", translate, StringComparison.Ordinal);
        Assert.True(entryText > translate);
    }

    [Fact]
    public void TheEquippedStripFallsBackToTheMuiToolbar()
    {
        var js = ReadEmbedded("sidebar.js");

        var injectHeader = js.IndexOf("function injectHeader()", StringComparison.Ordinal);
        var legacy = js.IndexOf("querySelector('.headerRight')", injectHeader, StringComparison.Ordinal);
        var modern = js.IndexOf("button[aria-controls=\"app-user-menu\"]", legacy, StringComparison.Ordinal);
        Assert.True(injectHeader >= 0);
        Assert.True(legacy > injectHeader);
        Assert.True(modern > legacy);
    }

    [Theory]
    [InlineData("sidebar.js")]
    [InlineData("standalone.js")]
    [InlineData("enhance.js")]
    [InlineData("Pages.index.html")]
    [InlineData("configPage.html")]
    public void EveryAuthenticatedCallSendsTheCurrentAuthorizationHeader(string asset)
    {
        // Jellyfin 12.0 runs a migration (DisableLegacyAuthorization) that
        // refuses X-Emby-Token, X-MediaBrowser-Token and api_key in the
        // query. Every plugin call that carried only the legacy header
        // answered 401 on 12: badges, preferences, friends, the admin
        // catalog. The current form is the only one 12 accepts, and 10.11
        // accepts both, so both are sent.
        var text = ReadEmbedded(asset);

        Assert.Contains("MediaBrowser Token=\"'", text, StringComparison.Ordinal);
        // The legacy header never travels alone any more: each place that
        // sets it also sets Authorization within the same block.
        var sites = 0;
        foreach (var needle in new[] { "['X-Emby-Token'] =", "['X-Emby-Token']=" })
        {
            var index = text.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                sites++;
                var windowStart = Math.Max(0, index - 400);
                Assert.Contains("Authorization", text.Substring(windowStart, index - windowStart), StringComparison.Ordinal);
                index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }
        }
        Assert.True(sites > 0, "the asset sets no token header at all");
    }

    [Fact]
    public void TheRevampAdminPageStaysInsideTheDashboardContentArea()
    {
        // The Revamp admin page takes the viewport with position: fixed.
        // The MUI dashboard keeps a docked 240px drawer and a fixed app bar
        // on layers above it, so the left 240px and the top 48px of the page
        // were hidden. The bootstrap measures both and the stylesheet
        // insets the page by them.
        var html = ReadEmbedded("Pages.index.html");
        var css = ReadEmbedded("styles-revamp.css");

        Assert.Contains("function fitDashboardShell()", html, StringComparison.Ordinal);
        Assert.Contains("querySelector('.MuiDrawer-docked')", html, StringComparison.Ordinal);
        Assert.Contains("querySelector('.dashboard-appBar')", html, StringComparison.Ordinal);
        Assert.Contains("setProperty('--ab-dash-left'", html, StringComparison.Ordinal);
        Assert.Contains("setProperty('--ab-dash-top'", html, StringComparison.Ordinal);
        // Re-measured after layout settles, not only on the resize event.
        Assert.Contains("new ResizeObserver(function () { measureDashboardShell(); })", html, StringComparison.Ordinal);

        var apply = html.IndexOf("function applyBodyClassAndWatch()", StringComparison.Ordinal);
        var fit = html.IndexOf("fitDashboardShell();", apply, StringComparison.Ordinal);
        var remove = html.IndexOf("function removeBodyClassAndUnwatch()", StringComparison.Ordinal);
        var release = html.IndexOf("releaseDashboardShell();", remove, StringComparison.Ordinal);
        Assert.True(apply >= 0 && fit > apply);
        Assert.True(remove >= 0 && release > remove);

        Assert.Contains("body.ab-revamp-fullwidth.ab-dash-shell #AchievementBadgesPage", css, StringComparison.Ordinal);
        Assert.Contains("inset: var(--ab-dash-top, 48px) 0 0 var(--ab-dash-left, 240px) !important;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTabIsNamedAfterThePageWhileTheRouteIsOurs()
    {
        // The SPA renders its "Page not found" route under the overlay and
        // names the tab after it.
        var js = ReadEmbedded("standalone.js");

        Assert.Contains("function applyDocumentTitle()", js, StringComparison.Ordinal);
        Assert.Contains("if (document.title !== wanted) document.title = wanted;", js, StringComparison.Ordinal);

        var mount = js.IndexOf("function mountRoute(host)", StringComparison.Ordinal);
        var applied = js.IndexOf("applyDocumentTitle();", mount, StringComparison.Ordinal);
        var watched = js.IndexOf("watchDocumentTitle();", mount, StringComparison.Ordinal);
        Assert.True(mount >= 0 && applied > mount && watched > mount);

        // The 1.5s watchdog re-applies it, so a late title write by the SPA
        // never sticks.
        var watchdog = js.IndexOf("setInterval(function () {", js.IndexOf("function unmountRoute()", StringComparison.Ordinal), StringComparison.Ordinal);
        var reapplied = js.IndexOf("applyDocumentTitle();", watchdog, StringComparison.Ordinal);
        Assert.True(watchdog >= 0 && reapplied > watchdog);
    }

    [Theory]
    [InlineData("profile-card.html")]
    [InlineData("profile-card-blades.html")]
    [InlineData("profile-card-metro.html")]
    [InlineData("configPage.html")]
    [InlineData("standalone.js")]
    [InlineData("enhance.js")]
    [InlineData("Pages.index.html")]
    public void NoAssetLinksToTheDeprecatedBangRoutes(string asset)
    {
        // jellyfin-web 12 still redirects #!/ routes, with a console
        // warning that the format will stop working; the routes have been
        // #/ since 10.9. sidebar.js keeps both spellings on purpose in its
        // login route checks, so it is not in this list.
        var text = ReadEmbedded(asset);
        Assert.DoesNotContain("#!/", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAdminProfileCardKeepsItsSubtitleAndUsesTheWholeHeroForTheShowcase()
    {
        // Two things seen on a live server. The subtitle went back to
        // "Loading..." after the summary had written "Completion: 42.7%",
        // because the translation applier rewrites every [data-i18n]
        // element and the summary can land first. And the showcase sat in
        // the hero's left column, next to three wide buttons, so a 1400px
        // card showed two columns of badges.
        var html = ReadEmbedded("Pages.index.html");

        var completion = html.IndexOf("tr('achievements.completion', 'Completion')", StringComparison.Ordinal);
        var detached = html.IndexOf("profileSubtitle.removeAttribute('data-i18n');", completion, StringComparison.Ordinal);
        Assert.True(completion >= 0 && detached > completion && detached - completion < 600);
        Assert.Equal(3, CountOf(html, "profileSubtitle.removeAttribute('data-i18n');"));

        var actions = html.IndexOf("<div class=\"abHeroActions\">", StringComparison.Ordinal);
        var showcase = html.IndexOf("<div id=\"abShowcaseWrap\"", StringComparison.Ordinal);
        Assert.True(actions >= 0 && showcase > actions);
        Assert.Contains(".abShowcaseWrap{\nmargin-top:1.1em;\nflex-basis:100%;", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:repeat(auto-fill,minmax(300px,1fr));", html, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    [Fact]
    public void TheLegacyDrawerInjectionStays()
    {
        // Jellyfin 12 still offers desktop-legacy, mobile-legacy and tv
        // layouts, all on the old drawer.
        var js = ReadEmbedded("sidebar.js");
        Assert.Contains("function injectSidebar()", js, StringComparison.Ordinal);
        Assert.Contains("querySelectorAll('.navMenuOption')", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEquippedStripPrefersAHeaderSomeoneCanSee()
    {
        // Measured on 12.0.0: .headerRight still exists inside the hidden
        // .skinHeader, so "found .headerRight" put the strip where nobody
        // could see it and the MUI fallback never ran. The visible target
        // wins, and a strip already mounted into the hidden header is moved.
        var js = ReadEmbedded("sidebar.js");

        var injectHeader = js.IndexOf("function injectHeader()", StringComparison.Ordinal);
        var shown = js.IndexOf("function shown(el){ return !!(el && el.offsetParent!==null); }", injectHeader, StringComparison.Ordinal);
        var choose = js.IndexOf("shown(legacyRight) ? legacyRight : (shown(modernRight) ? modernRight", shown, StringComparison.Ordinal);
        var move = js.IndexOf("headerRight.parentElement.insertBefore(existing, headerRight);", choose, StringComparison.Ordinal);
        Assert.True(injectHeader >= 0 && shown > injectHeader && choose > shown && move > choose);
    }

    [Fact]
    public void TheUserIdPollStopsBeforeItsLastDitchRequest()
    {
        // fetchJson waits up to 2s for a token. While it waited, the poll
        // that had just given up kept ticking and queued a fresh Users/Me
        // every 200ms: ~25 requests per caller on the login page, all 401.
        var js = ReadEmbedded("standalone.js");

        var poll = js.IndexOf("function getCurrentUserId()", StringComparison.Ordinal);
        var giveUp = js.IndexOf("if (attempts >= MAX_ATTEMPTS) {", poll, StringComparison.Ordinal);
        var stop = js.IndexOf("clearInterval(timer); timer = null;", giveUp, StringComparison.Ordinal);
        var lastDitch = js.IndexOf("fetchJson('Users/Me')", giveUp, StringComparison.Ordinal);
        Assert.True(poll >= 0 && giveUp > poll && stop > giveUp && stop < lastDitch);
    }

    [Fact]
    public void TheAdminPageDoesNotHardcodeVersionsAnyMore()
    {
        var html = ReadEmbedded("Pages.index.html");

        // The Revamp stylesheet is served immutable for a day, keyed by this
        // token. A literal token stops busting the moment it goes stale; the
        // injected bootstrap tag carries the one the running DLL stamped.
        Assert.DoesNotContain("var CSS_BUST = 'v=", html, StringComparison.Ordinal);
        Assert.Contains("script[src*=\"client-script/sidebar\"]", html, StringComparison.Ordinal);

        // The standalone page and sidebar.js each linked the same stylesheet
        // under their own literal token. Three copies of one bug.
        var standalone = ReadEmbedded("standalone.js");
        Assert.DoesNotContain("REVAMP_CSS_BUST = 'v=", standalone, StringComparison.Ordinal);
        Assert.Contains("script[src*=\"client-script/standalone\"]", standalone, StringComparison.Ordinal);
        var sidebar = ReadEmbedded("sidebar.js");
        Assert.DoesNotContain("__abCssBust = 'v=", sidebar, StringComparison.Ordinal);
        Assert.Contains("document.currentScript", sidebar, StringComparison.Ordinal);

        // The hero used to announce v1.9.2 on Jellyfin ABI 10.11.0.0 forever.
        Assert.DoesNotContain("version: '1.9.2'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("targetAbi: '10.11.0.0'", html, StringComparison.Ordinal);
        Assert.Contains("heroMetaUrl('Plugins')", html, StringComparison.Ordinal);
        Assert.Contains("heroMetaUrl('System/Info/Public')", html, StringComparison.Ordinal);
    }
}
