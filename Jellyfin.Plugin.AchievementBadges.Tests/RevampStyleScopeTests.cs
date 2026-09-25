using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #133: the Revamp stylesheet is injected into the whole web client and
/// the attribute it keys off is stamped on &lt;body&gt;, so any rule that names a
/// generic element restyles Jellyfin itself and every other plugin's settings
/// page. A vanilla checkbox went invisible and text inputs lost their border,
/// their height and their label spacing. These pin the scope.
/// </summary>
public class RevampStyleScopeTests
{
    private const string Attribute = "[data-ab-style=\"revamp\"]";

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every selector in the sheet, comments stripped and selector
    /// lists split, so one rule with five selectors is five entries.</summary>
    private static IEnumerable<string> Selectors()
    {
        var css = Regex.Replace(ReadEmbedded("styles-revamp.css"), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(css, @"(?:^|\})\s*([^{}@][^{}]*?)\s*\{", RegexOptions.Multiline))
        {
            foreach (var part in m.Groups[1].Value.Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) yield return s;
            }
        }
    }

    /// <summary>The two roots that declare the Revamp design tokens, plus our
    /// own prefixes. A selector anchored to any of these cannot reach
    /// Jellyfin's markup.</summary>
    private static bool IsOurs(string firstDescendant) =>
        firstDescendant.StartsWith("#ab", StringComparison.Ordinal) ||
        firstDescendant.StartsWith("#Achievement", StringComparison.Ordinal) ||
        firstDescendant.StartsWith("#achievement", StringComparison.Ordinal) ||
        firstDescendant.StartsWith(".ab", StringComparison.Ordinal) ||
        firstDescendant.StartsWith(".rv", StringComparison.Ordinal) ||
        firstDescendant.StartsWith("[data-ab", StringComparison.Ordinal);

    [Fact]
    public void No_rule_reaches_outside_our_surfaces_through_the_body_attribute()
    {
        // sidebar.js stamps the attribute on <body> so the Friends drawer and
        // the floating button can style themselves. That makes a bare
        // "[data-ab-style=revamp] <element>" rule global.
        var escapees = new List<string>();
        foreach (var selector in Selectors())
        {
            if (!selector.StartsWith(Attribute, StringComparison.Ordinal)) continue;
            var rest = selector.Substring(Attribute.Length).Trim();
            if (rest.Length == 0) continue;           // the token block itself
            var first = rest.Split(' ', '>', '+', '~')[0];
            if (!IsOurs(first)) escapees.Add(selector);
        }

        Assert.True(escapees.Count == 0,
            "These would style Jellyfin's own UI: " + string.Join(" | ", escapees.Take(10)));
    }

    [Theory]
    [InlineData("input[type=\"checkbox\"]")]
    [InlineData("input[type=\"text\"]")]
    [InlineData("::selection")]
    [InlineData(":focus-visible")]
    public void The_generic_element_rules_are_anchored_to_a_root_that_declares_the_tokens(string element)
    {
        // Anchoring matters twice over: it keeps the rules off Jellyfin's
        // markup, and it keeps them where var(--rv-*) actually resolves.
        // Unanchored, the checkbox rule kept appearance:none and lost its
        // border and background, which is what made it invisible.
        // The bare form is the leak: the attribute, a space, and the element,
        // with nothing of ours in between. A selector like
        // ".abFilterChip:focus-visible" is already anchored by our own class.
        var bare = Attribute + " " + element;
        Assert.DoesNotContain(bare, Selectors());

        var anchored = Selectors().Where(s =>
            s.EndsWith(" " + element, StringComparison.Ordinal) &&
            (s.Contains("#AchievementBadgesPage", StringComparison.Ordinal) ||
             s.Contains("#achievementBadgesStandaloneRoot", StringComparison.Ordinal)))
            .ToList();
        Assert.True(anchored.Count > 0, "no anchored rule left for " + element);
    }

    [Fact]
    public void The_design_tokens_are_declared_on_exactly_those_roots()
    {
        // If a future block declares them somewhere broader, the anchors above
        // stop being the whole story and this test should be revisited.
        var css = ReadEmbedded("styles-revamp.css");
        var declaring = Selectors()
            .Where(s => s.Contains("data-ab-style", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(declaring, s => s.Contains("#AchievementBadgesPage", StringComparison.Ordinal));
        Assert.Contains(declaring, s => s.Contains("#achievementBadgesStandaloneRoot", StringComparison.Ordinal));
        Assert.Contains("--rv-accent:", css, StringComparison.Ordinal);
    }
}
