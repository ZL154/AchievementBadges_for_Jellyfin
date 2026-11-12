using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Regression test for issue #179. A link does not take the colour of the
/// text around it: the browser paints it in its own link blue. The standalone
/// page coloured the "Open shareable profile card" link only under the light
/// page theme, so on the default dark page, the dark theme and every shop
/// theme it kept the browser's blue, dimmed further by the 70% opacity of
/// <c>.ab-muted</c>. On the default page the link came out at 1.24:1 against
/// the profile card with the light scheme #0000EE the issue shows, and at
/// 3.38:1 to 3.93:1 with the lighter blues Firefox and Chromium switch to for
/// <c>color-scheme: dark</c>.
/// </summary>
public class ProfileCardLinkContrastTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_profile_card_link_takes_the_colour_of_the_page_text()
    {
        var standalone = ReadEmbedded("standalone.js");

        // No theme class in the selector, so it reaches the link on the
        // default page and under every theme. inherit follows whatever text
        // colour the theme sets, which a fixed colour would not; the light
        // theme keeps its own grey through its more specific rule.
        var rule = Regex.Match(standalone, @"'#' \+ ROOT_ID \+ ' #abSaProfileCardLink\{(?<body>[^}]*)\}'");
        Assert.True(rule.Success, "the profile card link has no colour outside the light theme");
        Assert.Contains("color:inherit;", rule.Groups["body"].Value, StringComparison.Ordinal);
    }
}
