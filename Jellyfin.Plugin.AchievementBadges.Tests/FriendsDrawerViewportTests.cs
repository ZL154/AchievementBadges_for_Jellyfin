using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #147: on a phone the chat's message box sat behind the navigation
/// bar. The Friends drawer was 100vh tall, which on a phone is the height
/// with the address bar hidden, so while the bar was on screen the bottom of
/// the drawer ran past the visible area. Jellyfin's viewport also uses
/// viewport-fit=cover, which lets the page draw under a gesture bar, and
/// nothing kept the drawer or the Friends button clear of it. These hold the
/// drawer to the visible height and both to the safe-area insets.
/// </summary>
public class FriendsDrawerViewportTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // The declarations of one rule in the stylesheet sidebar.js injects.
    // Each rule is its own string literal, so the selector follows a quote.
    private static string Rule(string js, string selector)
    {
        var start = js.IndexOf("'" + selector + "{", StringComparison.Ordinal);
        Assert.True(start >= 0, selector + " rule not found in sidebar.js");
        var open = start + selector.Length + 2;
        return js.Substring(open, js.IndexOf('}', open) - open);
    }

    // The value of a declaration, not of a longer property ending in the
    // same name (height, not min-height).
    private static string Declaration(string rule, string property)
    {
        var match = Regex.Match(rule, "(?:^|;)" + Regex.Escape(property) + ":([^;]*)");
        Assert.True(match.Success, property + " not declared in: " + rule);
        return match.Groups[1].Value;
    }

    private static string Between(string js, string from, string to)
    {
        var start = js.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from + " not found in sidebar.js");
        var end = js.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, to + " not found after " + from);
        return js.Substring(start, end - start);
    }

    [Fact]
    public void The_drawer_is_as_tall_as_the_visible_viewport()
    {
        var drawer = Rule(ReadEmbedded("sidebar.js"), "#abFriendsDrawer");

        // 100% of a fixed box follows the address bar; 100vh does not.
        Assert.Equal("100%", Declaration(drawer, "height"));
    }

    [Fact]
    public void The_drawer_content_stays_inside_the_safe_area()
    {
        var js = ReadEmbedded("sidebar.js");
        var drawer = Rule(js, "#abFriendsDrawer");

        // Border-box, or the padding would push the drawer past the bottom
        // again instead of shrinking the content.
        Assert.Equal("border-box", Declaration(drawer, "box-sizing"));

        var padding = Declaration(drawer, "padding");
        Assert.Contains("env(safe-area-inset-top,0px)", padding, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom,0px)", padding, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-left,0px)", padding, StringComparison.Ordinal);

        // applyCorner() anchors the drawer to the right edge for the right
        // corners, and the side inset has to move with it.
        Assert.Contains("drawer.dataset.abSlideDir = slideFromLeft ? 'left' : 'right';", js, StringComparison.Ordinal);
        var right = Rule(js, "#abFriendsDrawer[data-ab-slide-dir=\"right\"]");
        Assert.Equal("0", Declaration(right, "padding-left"));
        Assert.Equal("env(safe-area-inset-right,0px)", Declaration(right, "padding-right"));
    }

    [Fact]
    public void The_button_keeps_clear_of_the_safe_area_in_every_corner()
    {
        var js = ReadEmbedded("sidebar.js");

        // The stylesheet places the button until applyCorner() runs. The
        // plain offset comes first so a browser without env() keeps it.
        var button = Rule(js, "#abFriendsBtn");
        Assert.Contains("left:1em;left:calc(1em + env(safe-area-inset-left,0px));", button, StringComparison.Ordinal);
        Assert.Contains("bottom:1.2em;bottom:calc(1.2em + env(safe-area-inset-bottom,0px));", button, StringComparison.Ordinal);

        // applyCorner() sets the position inline, which beats the stylesheet,
        // so every offset it gives goes through the helper; only the resets
        // to auto are plain.
        var applyCorner = Between(js, "function applyCorner(corner){", "function ensureFriendsDrawer(");
        Assert.Empty(Regex.Matches(applyCorner, @"btn\.style\.(?:left|right|top|bottom) = '(?!auto')"));
        Assert.Equal(8, Regex.Matches(applyCorner, @"setSafeOffset\(btn, '(?:left|right|top|bottom)', '1(?:\.2)?em'\)").Count);

        // Same fallback order inside the helper: a rejected assignment
        // leaves the previous value in place.
        var helper = Between(js, "function setSafeOffset(el, side, base){", "function applyCorner(corner){");
        var plain = helper.IndexOf("el.style[side] = base;", StringComparison.Ordinal);
        var safe = helper.IndexOf("el.style[side] = 'calc(' + base + ' + env(safe-area-inset-' + side + ', 0px))';", StringComparison.Ordinal);
        Assert.True(plain >= 0, "the helper does not set the plain offset");
        Assert.True(safe > plain, "the safe-area offset must be assigned after the plain one");
    }
}
