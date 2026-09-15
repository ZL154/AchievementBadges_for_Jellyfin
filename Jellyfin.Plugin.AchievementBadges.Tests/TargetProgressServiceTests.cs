using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AchievementBadges.Api;
using Jellyfin.Plugin.AchievementBadges.Configuration;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Issue #107. Two things are worth pinning here and one is not. The
/// percentage arithmetic is pure and gets a real test, including the zero
/// case that would otherwise hand out a badge for an empty container. The
/// wiring gets a reflection test, the way LibraryCompletionWiringTests pins
/// the backfill: a dependency silently dropped from the constructor is how
/// this codebase has produced quietly-stuck badges twice before. Exercising
/// the queries themselves needs a live library, so that evidence belongs in
/// the PR as a recorded run, not in a mock here.
/// </summary>
public class TargetProgressServiceTests
{
    [Fact]
    public void AnEmptyContainerIsNotComplete()
    {
        // Zero of zero reads as 100 in any naive percentage, which would hand
        // out "you finished it" for an empty collection. LibraryCompletion
        // already skips on total == 0 for the same reason.
        Assert.Null(TargetProgressService.Percent(total: 0, played: 0));
    }

    [Fact]
    public void PercentagesRoundTheWayTheRestOfThePluginDoes()
    {
        Assert.Equal(0, TargetProgressService.Percent(10, 0));
        Assert.Equal(50, TargetProgressService.Percent(10, 5));
        Assert.Equal(100, TargetProgressService.Percent(10, 10));
        // 1 of 3 is 33.33, rounds to 33; 2 of 3 is 66.67, rounds to 67.
        Assert.Equal(33, TargetProgressService.Percent(3, 1));
        Assert.Equal(67, TargetProgressService.Percent(3, 2));
    }

    [Fact]
    public void ASingleItemContainerCanReachOneHundred()
    {
        // Deliberately different from ArtistCompletionPercent, which skips
        // one-track artists because it computes every artist automatically.
        // Targets are chosen by hand, so a one-item target is a choice.
        Assert.Equal(100, TargetProgressService.Percent(1, 1));
    }

    [Fact]
    public void TheServiceCanReachTheLibraryAndTheUserData()
    {
        // Play count comes from Jellyfin's own UserItemData, which is what
        // makes "play this 3005 times" work against history that already
        // exists. Losing IUserDataManager here would silently reduce the
        // metric to zero for everyone.
        var constructor = typeof(TargetProgressService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ILibraryManager), parameterTypes);
        Assert.Contains(typeof(IUserDataManager), parameterTypes);
        Assert.Contains(typeof(CustomBadgeService), parameterTypes);
        Assert.Contains(typeof(AchievementBadgeService), parameterTypes);
    }

    [Fact]
    public void TheTargetCapHasASaneDefault()
    {
        Assert.Equal(50, new PluginConfiguration().MaxTargetedBadgeTargets);
    }

    // [issue #129] The per-play membership check no longer needs one library
    // query per target: the played item's ancestors are read once and each
    // hierarchical target is a set test. These pin the decision table.
    [Fact]
    public void ANameOnlyTargetAlwaysGoesToCompute()
    {
        var target = new ObservedTarget(AchievementMetric.ContainerCompletionPercent, "Breaking Bad", Guid.Empty, "Breaking Bad");
        Assert.True(TargetProgressService.DecideWithoutLibrary(target, Guid.NewGuid()));
    }

    [Fact]
    public void AnItemTargetIsAPlainIdComparison()
    {
        var id = Guid.NewGuid();
        var target = new ObservedTarget(AchievementMetric.ItemPlayCount, id.ToString("N"), id, "Free Bird");
        Assert.True(TargetProgressService.DecideWithoutLibrary(target, id));
        Assert.False(TargetProgressService.DecideWithoutLibrary(target, Guid.NewGuid()));
    }

    [Fact]
    public void AContainerTargetIsLeftToTheLibrary()
    {
        var target = new ObservedTarget(AchievementMetric.ContainerCompletionPercent, "x", Guid.NewGuid(), "The Wire");
        Assert.Null(TargetProgressService.DecideWithoutLibrary(target, Guid.NewGuid()));
    }

    [Fact]
    public void AHierarchicalTargetContainsTheItemWhenItIsAnAncestor()
    {
        var series = Guid.NewGuid();
        var season = Guid.NewGuid();
        var ancestors = new HashSet<Guid> { season, series, Guid.NewGuid() };
        Assert.True(TargetProgressService.UnderAncestor(series, ancestors));
        Assert.True(TargetProgressService.UnderAncestor(season, ancestors));
        Assert.False(TargetProgressService.UnderAncestor(Guid.NewGuid(), ancestors));
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(-7, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 500)]
    [InlineData(5000, 1000)]
    public void TheCapIsClampedToTheBoundsTheFeatureWasSizedFor(int? configured, int expected)
    {
        Assert.Equal(expected, TargetProgressService.EffectiveCap(configured));
    }

    // [issue #129] The settings page reports the cap from the same walk that
    // decides what gets computed, so what the admin sees is what runs.
    [Fact]
    public void TheSummaryCountsDistinctTargetsAndNamesTheOnesPastTheCap()
    {
        var wire = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var badges = new[]
        {
            Targeted("The Wire (complete)", wire, "The Wire"),
            Targeted("The Wire (again)", wire, "The Wire"),
            Targeted("Breaking Bad (complete)", bad, "Breaking Bad"),
            Targeted("Sopranos (complete)", Guid.NewGuid(), "The Sopranos"),
            Targeted("Disabled one", Guid.NewGuid(), "Never counted", enabled: false),
        };

        var summary = TargetProgressService.Summarize(badges, 2);

        Assert.Equal(2, summary.Configured);
        Assert.Equal(2, summary.Cap);
        Assert.Equal(2, summary.Observed);
        Assert.Equal(new[] { "The Sopranos" }, summary.Dropped);
    }

    [Fact]
    public void TheSummaryReportsTheClampedCapNextToTheConfiguredOne()
    {
        var summary = TargetProgressService.Summarize(Array.Empty<CustomBadge>(), 5000);
        Assert.Equal(5000, summary.Configured);
        Assert.Equal(TargetProgressService.MaxTargetCap, summary.Cap);
        Assert.Equal(0, summary.Observed);
        Assert.Empty(summary.Dropped);
    }

    [Fact]
    public void TheCustomBadgesControllerExposesTheTargetSummary()
    {
        var method = typeof(CustomBadgesController).GetMethod("Targets");
        Assert.NotNull(method);
        var route = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.HttpGetAttribute>().Single();
        Assert.Equal("targets", route.Template);
    }

    [Fact]
    public void TheCustomBadgesControllerLetsTheAdminSetTheCap()
    {
        var method = typeof(CustomBadgesController).GetMethod("SetTargetCap");
        Assert.NotNull(method);
        var route = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Single();
        Assert.Equal("targets", route.Template);
        Assert.Equal(typeof(TargetCapRequest), method.GetParameters().Single().ParameterType);
    }

    private static CustomBadge Targeted(string badgeName, Guid targetId, string targetName, bool enabled = true)
    {
        return new CustomBadge
        {
            Name = badgeName,
            Enabled = enabled,
            Criteria = new CustomBadgeCriteria
            {
                Metric = AchievementMetric.ContainerCompletionPercent,
                MetricParameter = targetId.ToString("N") + "|" + targetName,
            },
        };
    }

    // [issue #129] The settings page wires the cap to the two endpoints above.
    [Fact]
    public void TheSettingsPageWiresTheTargetCapToTheApi()
    {
        var html = ReadEmbedded("Pages.index.html");
        var en = ReadEmbedded("Pages.translations.en.json");

        foreach (var id in new[] { "abTargetCapValue", "abTargetCapSaveBtn", "abTargetCapStatus", "abTargetCapSummary" })
        {
            Assert.Contains("id=\"" + id + "\"", html);
        }

        Assert.Contains("fetchJson('Plugins/AchievementBadges/custom-badges/targets')", html);
        Assert.Contains("fetchJson('Plugins/AchievementBadges/custom-badges/targets', 'POST', { MaxTargets: value })", html);
        Assert.Contains("min=\"1\" max=\"1000\"", html);

        foreach (var key in new[] { "admin.targets.cap_label", "admin.targets.cap_help", "admin.targets.summary", "admin.targets.clamped", "admin.targets.dropped", "admin.targets.invalid" })
        {
            Assert.Contains("\"" + key + "\":", en);
            // Static labels carry the key in data-i18n; dynamic strings pass it to tr().
            Assert.True(html.Contains("data-i18n=\"" + key + "\"", StringComparison.Ordinal) || html.Contains("tr('" + key + "'", StringComparison.Ordinal), key + " is not used by the page");
        }
    }

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}
