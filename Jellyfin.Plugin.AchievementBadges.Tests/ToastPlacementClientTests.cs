using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #136 on the client side. The six placements live in four places:
/// the server's normaliser, enhance.js (which positions the container), the
/// user's settings select and the admin default select. A placement missing
/// from any one of them is either unreachable or silently rendered in the
/// wrong corner, so the first test holds all four to the same list.
/// </summary>
public class ToastPlacementClientTests
{
    private static readonly string[] Placements =
    {
        "top-right", "top-center", "top-left", "bottom-right", "bottom-center", "bottom-left",
    };

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Every_layer_offers_the_same_six_placements()
    {
        // Server: every placement survives normalisation as itself.
        foreach (var p in Placements)
        {
            Assert.Equal(p, ToastPlacement.NormalizeDefault(p));
            Assert.Equal(p, ToastPlacement.NormalizeUserChoice(p));
        }

        // enhance.js: the list it validates against, exactly.
        var enhance = ReadEmbedded("enhance.js");
        var listed = Regex.Match(enhance, @"var TOAST_PLACEMENTS = \[([^\]]*)\]");
        Assert.True(listed.Success, "TOAST_PLACEMENTS not found in enhance.js");
        var inEnhance = Regex.Matches(listed.Groups[1].Value, "'([a-z-]+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(Placements.OrderBy(x => x), inEnhance.OrderBy(x => x));

        // enhance.js: a container layout for every placement but the default
        // branch, which is top-right.
        foreach (var p in Placements.Where(p => p != "top-right"))
        {
            Assert.Contains("case '" + p + "':", enhance, StringComparison.Ordinal);
        }

        // The user's settings select.
        var standalone = ReadEmbedded("standalone.js");
        foreach (var p in Placements)
        {
            Assert.Contains("toastPosOption('" + p + "')", standalone, StringComparison.Ordinal);
        }

        // The admin default select.
        var admin = ReadEmbedded("Pages.index.html");
        var select = Regex.Match(admin, "<select id=\"abFcDefaultToastPosition\".*?</select>", RegexOptions.Singleline);
        Assert.True(select.Success, "the admin default toast select is missing");
        foreach (var p in Placements)
        {
            Assert.Contains("value=\"" + p + "\"", select.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Top_center_is_centred_the_way_bottom_center_is()
    {
        var enhance = ReadEmbedded("enhance.js");
        var line = enhance.Split('\n').Single(l => l.Contains("case 'top-center':", StringComparison.Ordinal));
        Assert.Contains("left: '50%'", line, StringComparison.Ordinal);
        Assert.Contains("translateX(-50%)", line, StringComparison.Ordinal);
        Assert.Contains("'align-items': 'center'", line, StringComparison.Ordinal);
        // Under the header, like the other top placements.
        Assert.Contains("top: '4.5em'", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_choice_follows_the_admin_default()
    {
        var enhance = ReadEmbedded("enhance.js");

        // The admin default is read from public-config, the only admin
        // setting a regular user's client can see.
        Assert.Contains("_adminToastPos = normalizeToastPos(cfg.DefaultToastPosition", enhance, StringComparison.Ordinal);

        // A user value that is not a placement falls back to it, not to a
        // hard coded corner.
        var resolve = enhance.Substring(enhance.IndexOf("function resolveToastPos(", StringComparison.Ordinal), 300);
        Assert.Contains("normalizeToastPos(_adminToastPos)", resolve, StringComparison.Ordinal);

        // Both the first load and a settings change go through it.
        Assert.Equal(2, Regex.Matches(enhance, @"_toastPos = resolveToastPos\(").Count);
    }

    [Fact]
    public void The_user_can_go_back_to_the_server_default()
    {
        var standalone = ReadEmbedded("standalone.js");

        // The first option is the empty value, which the server stores as
        // "no choice".
        var select = standalone.IndexOf("data-settings-select=\"toastPosition\"", StringComparison.Ordinal);
        Assert.True(select > 0);
        var body = standalone.Substring(select, 400);
        Assert.Contains("'<option value=\"\"'", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("'<option value=\"\"'", StringComparison.Ordinal) < body.IndexOf("toastPosOption('top-right')", StringComparison.Ordinal),
            "the server default has to come first");

        // And the stored value is no longer forced to top-right on the way in.
        Assert.DoesNotContain("prefs.ToastPosition || 'top-right'", standalone, StringComparison.Ordinal);
        Assert.DoesNotContain("prefs.toastPosition || prefs.ToastPosition || 'top-right'", standalone, StringComparison.Ordinal);
    }

    [Fact]
    public void The_admin_page_sends_and_reloads_the_default()
    {
        var admin = ReadEmbedded("Pages.index.html");
        Assert.Contains("DefaultToastPosition: (document.getElementById('abFcDefaultToastPosition')", admin, StringComparison.Ordinal);
        Assert.Contains("toastPosSel.value = c.DefaultToastPosition", admin, StringComparison.Ordinal);
    }
}
