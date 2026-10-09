using System;
using System.Linq;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// The merge of an item's genres, tags or studios with its series' that live
/// playback and the watch-history backfill share (#154).
/// </summary>
public class StringListsTests
{
    [Fact]
    public void Nothing_left_after_the_merge_is_null()
    {
        Assert.Null(StringLists.Union(null, null));
        Assert.Null(StringLists.Union(Array.Empty<string>(), Array.Empty<string>()));
        Assert.Null(StringLists.Union(new[] { " ", string.Empty }, null));
    }

    [Fact]
    public void Names_that_differ_only_by_case_appear_once_with_the_first_spelling()
    {
        var merged = StringLists.Union(new[] { "Drama", "HBO" }, new[] { "drama", "Crime", "hbo" });

        Assert.NotNull(merged);
        Assert.Equal(new[] { "Crime", "Drama", "HBO" }, merged!.OrderBy(s => s, StringComparer.Ordinal));
    }
}
