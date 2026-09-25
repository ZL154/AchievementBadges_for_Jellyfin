using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.AchievementBadges.Api;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// The admin feature-config endpoint is written by two saves on the admin
/// page and read back by the page to fill its controls. Each piece was tested
/// on its own and the wire between them was not, which let two bugs through:
/// the default UI style from #43 was posted but never bound, and the three
/// page integration flags were bound as plain bools that the main save never
/// sent, so every main save switched them off. These pin the round trip.
/// </summary>
public class FeatureConfigRoundTripTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The keys of the object literal the main feature-config save posts.</summary>
    private static string[] MainSaveKeys()
    {
        var html = ReadEmbedded("Pages.index.html");
        const string Start = "fetchJson('Plugins/AchievementBadges/admin/feature-config', 'POST', {";
        var from = html.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(from >= 0, "the main feature-config save was not found in the admin page");
        var to = html.IndexOf("}).then(", from, StringComparison.Ordinal);
        var body = html.Substring(from + Start.Length, to - from - Start.Length);
        return Regex.Matches(body, @"^\s*([A-Za-z]\w*)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
    }

    private static PropertyInfo[] RequestProperties() =>
        typeof(AchievementBadgesController.FeatureConfigRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static bool IsOptional(PropertyInfo p)
    {
        if (Nullable.GetUnderlyingType(p.PropertyType) is not null) return true;
        if (p.PropertyType.IsValueType) return false;
        // Reference types: only an explicit nullable annotation counts as
        // "omitted means keep". A string with a default is a reset in disguise.
        var context = new NullabilityInfoContext();
        return context.Create(p).WriteState == NullabilityState.Nullable;
    }

    [Fact]
    public void Every_field_the_admin_page_posts_is_bound()
    {
        // An unbound field is dropped by model binding without an error, and
        // the page reports "saved". That is how the #43 default style was
        // never stored.
        var bound = RequestProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unbound = MainSaveKeys().Where(k => !bound.Contains(k)).ToList();
        Assert.True(unbound.Count == 0, "posted but never bound: " + string.Join(", ", unbound));
    }

    [Fact]
    public void A_field_the_main_save_leaves_out_is_kept_not_reset()
    {
        // Anything the main save does not send arrives as the property's
        // default. For a plain bool that default is written over the stored
        // value, which is how enabling the Custom Tabs host and then editing
        // the welcome message switched the host off again.
        var sent = MainSaveKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resetOnSave = RequestProperties()
            .Where(p => !sent.Contains(p.Name) && !IsOptional(p))
            .Select(p => p.Name)
            .ToList();
        Assert.True(resetOnSave.Count == 0, "reset by every main save: " + string.Join(", ", resetOnSave));
    }

    [Fact]
    public void The_admin_page_can_read_back_what_it_saves()
    {
        // Every dependency is unused by this action, which only reads the
        // plugin configuration and falls back to defaults when there is none.
        var controller = new AchievementBadgesController(
            null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!);

        var result = Assert.IsType<OkObjectResult>(controller.GetFeatureConfig());
        var returned = result.Value!.GetType().GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var key in new[]
        {
            "DefaultUiStyle", "ForceDefaultUiStyle", "DefaultToastPosition",
            "EnableCustomTabsIntegration", "EnablePluginPagesIntegration", "EnableUserMenuShortcut",
        })
        {
            Assert.Contains(key, returned);
        }
    }

    [Fact]
    public void The_client_learns_the_admin_toast_default_from_public_config()
    {
        // [#136] A user whose ToastPosition is empty follows this value, and
        // public-config is the only admin setting a non admin client can read.
        var controller = new AchievementBadgesController(
            null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!);

        var result = Assert.IsType<OkObjectResult>(controller.GetPublicConfig());
        var prop = result.Value!.GetType().GetProperty("DefaultToastPosition");
        Assert.NotNull(prop);
        Assert.Equal("top-right", prop!.GetValue(result.Value));
    }
}
