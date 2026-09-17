using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #131: the Achievements entry in the Home tab bar showed a blank
/// page. Custom Tabs creates the tab button in the browser but injects the
/// tab's panel server-side, by matching one exact string in Jellyfin's home
/// page chunk. A theme that ships a reformatted copy of that chunk (Abyss's
/// Spotlight) breaks the match, so the panel never exists while the button
/// does, and our page has nothing to mount into. These pin the repair: the
/// plugin builds the missing panel itself, and does it early enough in the
/// watchdog tick to matter.
/// </summary>
public class CustomTabsPanelRepairTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Standalone() => ReadEmbedded("standalone.js");

    [Fact]
    public void The_repair_runs_with_the_rest_of_the_navigation_integration()
    {
        var js = Standalone();

        var definition = js.IndexOf("function repairCustomTabsPanel(", StringComparison.Ordinal);
        Assert.True(definition > 0, "the repair must exist");

        var apply = js.IndexOf("function applyNavigationIntegrationVisibility(", StringComparison.Ordinal);
        Assert.True(apply > 0);
        var applyBody = js.Substring(apply, 400);
        Assert.Contains("repairCustomTabsPanel(cfg, prefs);", applyBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_watchdog_repairs_before_it_looks_for_a_host()
    {
        // The repair puts the host in the DOM. Running it after the lookup
        // would work, but only from the following tick 1.5s later, which the
        // user sees as a tab that stays blank for a moment for no reason.
        var js = Standalone();
        // Scope this to the watchdog itself: the file has earlier timers, and
        // onRouteChange looks for a host too, so searching from the first
        // setInterval reads the wrong block.
        var end = js.IndexOf("}, 1500);", StringComparison.Ordinal);
        Assert.True(end > 0, "the 1.5s watchdog must still be there");
        var start = js.LastIndexOf("setInterval(function () {", end, StringComparison.Ordinal);
        Assert.True(start > 0);

        var body = js.Substring(start, end - start);
        var repair = body.IndexOf("applyNavigationIntegrationVisibility(publicConfigGlobal", StringComparison.Ordinal);
        var lookup = body.IndexOf("var host = findIntegrationHost();", StringComparison.Ordinal);
        Assert.True(repair > 0, "the watchdog must apply the navigation integration");
        Assert.True(lookup > 0, "the watchdog must still look for a host");
        Assert.True(repair < lookup, "the repair has to happen before the host lookup");
    }

    [Fact]
    public void The_panel_is_built_the_way_custom_tabs_builds_its_own()
    {
        // Jellyfin's tab machinery finds a panel by data-index and the two
        // content classes. A panel that misses any of them is dead markup.
        var js = Standalone();
        var create = js.IndexOf("function createCustomTabsPanel(", StringComparison.Ordinal);
        Assert.True(create > 0);
        var body = js.Substring(create, 1600);

        Assert.Contains("'tabContent pageTabContent'", body, StringComparison.Ordinal);
        Assert.Contains("'customTab_' + index", body, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('data-index')", body, StringComparison.Ordinal);
        Assert.Contains("data-ab-repaired-panel", body, StringComparison.Ordinal);
        Assert.Contains("CUSTOM_TABS_MARKER", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_created_when_there_is_no_orphaned_tab()
    {
        var js = Standalone();
        var create = js.IndexOf("function createCustomTabsPanel(", StringComparison.Ordinal);
        var body = js.Substring(create, 1600);

        // A button with no panel is the only case worth repairing: no button
        // means Custom Tabs has not run or the entry is gone, and an existing
        // panel means the injection worked.
        Assert.Contains("if (!button) return;", body, StringComparison.Ordinal);
        Assert.Contains("if (document.getElementById('customTab_' + index)) return;", body, StringComparison.Ordinal);

        var repair = js.IndexOf("function repairCustomTabsPanel(", StringComparison.Ordinal);
        var repairBody = js.Substring(repair, 1200);
        Assert.Contains("document.querySelector('[data-achievement-badges-host=\"custom-tabs\"]')", repairBody, StringComparison.Ordinal);
        Assert.Contains("if (!document.getElementById('indexPage')) return;", repairBody, StringComparison.Ordinal);
        // Admin switch and per-user preference both still gate the tab.
        Assert.Contains("EnableCustomTabsIntegration", repairBody, StringComparison.Ordinal);
        Assert.Contains("ShowCustomTabsEntry", repairBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Our_tab_is_identified_by_the_marker_in_the_custom_tabs_config()
    {
        // The panel id carries the entry's position in Custom Tabs' own
        // config, so the position is what has to be resolved, and the marker
        // is the only thing that identifies the entry as ours. A title match
        // would break the moment an admin renames the tab.
        var js = Standalone();
        var resolve = js.IndexOf("function getCustomTabsIndex(", StringComparison.Ordinal);
        Assert.True(resolve > 0);
        var body = js.Substring(resolve, 1400);

        Assert.Contains("fetchJson('CustomTabs/Config')", body, StringComparison.Ordinal);
        Assert.Contains("content.indexOf('data-achievement-badges-host=\"custom-tabs\"')", body, StringComparison.Ordinal);
        // Custom Tabs absent, or the request refused: resolve to -1 once so
        // the watchdog stops asking every 1.5 seconds.
        Assert.Contains("customTabsIndex = -1;", body, StringComparison.Ordinal);
    }
}
