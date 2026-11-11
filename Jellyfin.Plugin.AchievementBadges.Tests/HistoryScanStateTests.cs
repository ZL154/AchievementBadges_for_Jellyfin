using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;
using ScanFixture = Jellyfin.Plugin.AchievementBadges.Tests.TracearrNumericStringTests.ScanFixture;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

[Collection("Tracearr numeric strings")]
public class HistoryScanStateTests
{
    [Theory]
    [InlineData(nameof(UserAchievementProfile.LifetimeScore))]
    [InlineData(nameof(UserAchievementProfile.LastPlaybackAt))]
    [InlineData(nameof(UserAchievementProfile.ComboCount))]
    [InlineData(nameof(UserAchievementProfile.BestComboCount))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestId))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestDate))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestCompleted))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestStartValue))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestId))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestWeek))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestCompleted))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestStartValue))]
    [InlineData(nameof(UserAchievementProfile.DailyQuests))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuests))]
    [InlineData(nameof(UserAchievementProfile.CompletionMilestonesReached))]
    [InlineData(nameof(UserAchievementProfile.ActiveXpBoostUntil))]
    [InlineData(nameof(UserAchievementProfile.DoubleCreditPending))]
    [InlineData(nameof(UserAchievementProfile.LastLoginBonusDate))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestRerollsUsed))]
    [InlineData(nameof(UserAchievementProfile.DailyQuestRerollDate))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestRerollsUsed))]
    [InlineData(nameof(UserAchievementProfile.WeeklyQuestRerollWeek))]
    public void Scan_PreservesStateThatHistoryCannotReconstruct(string property)
    {
        using var fixture = new ScanFixture(true);
        var original = SeedState(fixture);
        using var expected = JsonSerializer.SerializeToDocument(original);
        Scan(fixture);
        using var actual = JsonSerializer.SerializeToDocument(fixture.Badges.PeekProfile(ScanFixture.UserId));
        Assert.Equal(expected.RootElement.GetProperty(property).GetRawText(), actual.RootElement.GetProperty(property).GetRawText());
    }

    [Fact]
    public void Scan_DeepCopiesQuestAndMilestoneLists_AndDoesNotAnnounceOldMilestones()
    {
        using var fixture = new ScanFixture(true);
        var original = SeedState(fixture);
        foreach (var badge in original.Badges) badge.Unlocked = true;
        Scan(fixture);
        var rebuilt = fixture.Badges.PeekProfile(ScanFixture.UserId)!;
        Assert.NotSame(original.DailyQuests, rebuilt.DailyQuests);
        Assert.NotSame(original.DailyQuests[0], rebuilt.DailyQuests[0]);
        Assert.NotSame(original.WeeklyQuests, rebuilt.WeeklyQuests);
        Assert.NotSame(original.WeeklyQuests[0], rebuilt.WeeklyQuests[0]);
        Assert.NotSame(original.CompletionMilestonesReached, rebuilt.CompletionMilestonesReached);
        rebuilt.DailyQuests[0].StartValue++;
        Assert.Equal(11, original.DailyQuests[0].StartValue);
        using var milestones = JsonSerializer.SerializeToDocument(fixture.Badges.CheckMilestones(ScanFixture.UserId));
        Assert.Empty(milestones.RootElement.GetProperty("NewlyReached").EnumerateArray());
    }

    [Fact]
    public void Scan_DoesNotSpendOrApplyPaidPowerUps_BonusesOrCombos()
    {
        using var fixture = new ScanFixture(true, enablePowerUps: true);
        var original = fixture.Badges.GetOrCreateProfileDirect(ScanFixture.UserId);
        var now = DateTimeOffset.UtcNow;
        var boostUntil = now.AddHours(1);
        original.ActiveXpBoostUntil = boostUntil;
        original.DoubleCreditPending = true;
        original.StreakFreezesBanked = 2;
        original.PowerUpInventory[nameof(PowerUpType.StreakFreeze)] = 2;
        original.LastLoginBonusDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        original.ComboCount = 3;
        original.BestComboCount = 7;
        original.LastPlaybackAt = now.AddMinutes(-5);
        SetHistory(fixture, 90, now.AddDays(-5).UtcDateTime, now.AddDays(-1).UtcDateTime, now.AddMinutes(-10).UtcDateTime);

        Scan(fixture);
        var rebuilt = fixture.Badges.PeekProfile(ScanFixture.UserId)!;
        Assert.True(rebuilt.DoubleCreditPending);
        Assert.Equal(boostUntil, rebuilt.ActiveXpBoostUntil);
        Assert.Equal(2, rebuilt.StreakFreezesBanked);
        Assert.Single(rebuilt.PowerUpInventory);
        Assert.Equal(2, rebuilt.PowerUpInventory[nameof(PowerUpType.StreakFreeze)]);
        Assert.Equal(3, rebuilt.Counters.MoviesWatched);
        Assert.Equal(15, rebuilt.ScoreBank);
        Assert.Equal(original.LastLoginBonusDate, rebuilt.LastLoginBonusDate);
        Assert.Equal(3, rebuilt.ComboCount);
        Assert.Equal(7, rebuilt.BestComboCount);
        Assert.Equal(original.LastPlaybackAt, rebuilt.LastPlaybackAt);

        fixture.Badges.RecordPlayback(new PlaybackContext
        {
            UserId = ScanFixture.UserId, IsMovie = true, PlayedAt = now,
            RunTimeTicks = TimeSpan.TicksPerMinute
        });
        Assert.False(rebuilt.DoubleCreditPending);
        Assert.Equal(boostUntil, rebuilt.ActiveXpBoostUntil);
        Assert.Equal(5, rebuilt.Counters.MoviesWatched);
        Assert.Equal(29, rebuilt.ScoreBank);
        Assert.Equal(2, rebuilt.StreakFreezesBanked);
        Assert.Single(rebuilt.PowerUpInventory);
        Assert.Equal(4, rebuilt.ComboCount);
        Assert.Equal(7, rebuilt.BestComboCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Scan_LastPlaybackAtKeepsTheLatestRealTimestamp(int historyOffsetHours)
    {
        using var fixture = new ScanFixture(true);
        var original = fixture.Badges.GetOrCreateProfileDirect(ScanFixture.UserId);
        var before = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
        original.LastPlaybackAt = before;
        var historical = before.AddHours(historyOffsetHours);
        SetHistory(fixture, 90, historical.UtcDateTime);
        Scan(fixture);
        Assert.Equal(historyOffsetHours > 0 ? historical : before,
            fixture.Badges.PeekProfile(ScanFixture.UserId)!.LastPlaybackAt);
    }

    [Fact]
    public void Scan_RebasesQuestStartValues_WithoutAwardingRecoveredHistoryAgain()
    {
        using var fixture = new ScanFixture(true);
        var profile = fixture.Badges.GetOrCreateProfileDirect(ScanFixture.UserId);
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var week = ISOWeek.GetYear(DateTime.Today) + "-W" + ISOWeek.GetWeekOfYear(DateTime.Today).ToString("D2", CultureInfo.InvariantCulture);
        Plugin.Instance!.Configuration.CustomDailyQuests.Add(new QuestDefinition
        {
            Id = "daily-minutes", Title = "Minutes", Metric = AchievementMetric.TotalMinutesWatched, Target = 50, Reward = 30
        });
        profile.Counters.TotalMinutesWatched = 100;
        profile.DailyQuestId = "daily-minutes";
        profile.DailyQuestDate = today;
        profile.DailyQuestStartValue = 90;
        profile.WeeklyQuestId = "weekly-10-hours";
        profile.WeeklyQuestWeek = week;
        profile.WeeklyQuestStartValue = 100;
        profile.DailyQuests = new List<QuestState>
        {
            new() { Id = "daily-minutes", Period = today, StartValue = 90 },
            new() { Id = "daily-watch-any", Period = today, Completed = true },
            new() { Id = "daily-genre", Period = today, Completed = true }
        };
        profile.WeeklyQuests = new List<QuestState>
        {
            new() { Id = "weekly-10-hours", Period = week, StartValue = 100 },
            new() { Id = "weekly-5-movies", Period = week, Completed = true },
            new() { Id = "weekly-3-genres", Period = week, Completed = true }
        };
        SetHistory(fixture, 700, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        Scan(fixture);
        var rebuilt = fixture.Badges.PeekProfile(ScanFixture.UserId)!;
        Assert.Equal(700, rebuilt.Counters.TotalMinutesWatched);
        Assert.Equal(690, rebuilt.DailyQuestStartValue);
        Assert.Equal(700, rebuilt.WeeklyQuestStartValue);
        Assert.Equal(690, rebuilt.DailyQuests[0].StartValue);
        Assert.Equal(700, rebuilt.WeeklyQuests[0].StartValue);
        Scan(fixture);
        rebuilt = fixture.Badges.PeekProfile(ScanFixture.UserId)!;
        Assert.Equal(690, rebuilt.DailyQuests[0].StartValue);
        Assert.Equal(700, rebuilt.WeeklyQuests[0].StartValue);
        var quests = new QuestService(fixture.Badges);
        var bank = rebuilt.ScoreBank;
        using var daily = JsonSerializer.SerializeToDocument(quests.GetOrCreateDailyList(ScanFixture.UserId));
        using var weekly = JsonSerializer.SerializeToDocument(quests.GetOrCreateWeeklyList(ScanFixture.UserId));
        Assert.Equal(10, daily.RootElement[0].GetProperty("Current").GetInt32());
        Assert.Equal(0, weekly.RootElement[0].GetProperty("Current").GetInt32());
        Assert.False(rebuilt.DailyQuests[0].Completed);
        Assert.False(rebuilt.WeeklyQuests[0].Completed);
        Assert.Equal(bank, rebuilt.ScoreBank);

        fixture.Badges.RecordPlayback(new PlaybackContext
        {
            UserId = ScanFixture.UserId, IsMovie = true, RunTimeTicks = 600 * TimeSpan.TicksPerMinute
        });
        bank = rebuilt.ScoreBank;
        quests.GetOrCreateDailyList(ScanFixture.UserId);
        quests.GetOrCreateWeeklyList(ScanFixture.UserId);
        Assert.Equal(bank + 230, rebuilt.ScoreBank);
        quests.GetOrCreateDailyList(ScanFixture.UserId);
        quests.GetOrCreateWeeklyList(ScanFixture.UserId);
        Assert.Equal(bank + 230, rebuilt.ScoreBank);
    }

    private static UserAchievementProfile SeedState(ScanFixture fixture)
    {
        var p = fixture.Badges.GetOrCreateProfileDirect(ScanFixture.UserId);
        p.LifetimeScore = 12345;
        p.LastPlaybackAt = DateTimeOffset.UtcNow.AddHours(-1);
        p.ComboCount = 3;
        p.BestComboCount = 7;
        p.DailyQuestId = "daily-binge-lite";
        p.DailyQuestDate = "2026-01-01";
        p.DailyQuestCompleted = true;
        p.DailyQuestStartValue = 11;
        p.WeeklyQuestId = "weekly-10-hours";
        p.WeeklyQuestWeek = "2026-W01";
        p.WeeklyQuestCompleted = true;
        p.WeeklyQuestStartValue = 22;
        p.DailyQuests.Add(new QuestState { Id = p.DailyQuestId, Period = p.DailyQuestDate, Completed = true, StartValue = 11 });
        p.WeeklyQuests.Add(new QuestState { Id = p.WeeklyQuestId, Period = p.WeeklyQuestWeek, Completed = true, StartValue = 22 });
        p.CompletionMilestonesReached.AddRange(new[] { 25, 50, 75, 100 });
        p.ActiveXpBoostUntil = DateTimeOffset.UtcNow.AddHours(1);
        p.DoubleCreditPending = true;
        p.LastLoginBonusDate = "2026-01-01";
        p.DailyQuestRerollsUsed = 2;
        p.DailyQuestRerollDate = "2026-01-01";
        p.WeeklyQuestRerollsUsed = 3;
        p.WeeklyQuestRerollWeek = "2026-W01";
        return p;
    }

    private static void SetHistory(ScanFixture fixture, int minutes, params DateTime[] dates)
    {
        var items = dates.Select(_ => (BaseItem)new Movie { Id = Guid.NewGuid(), RunTimeTicks = minutes * TimeSpan.TicksPerMinute }).ToArray();
        var byId = items.Select((item, i) => (item.Id, Date: dates[i])).ToDictionary(x => x.Id, x => x.Date);
        fixture.Library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => new QueryResult<BaseItem>(
                q.IsPlayed == true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? items : Array.Empty<BaseItem>()));
        fixture.UserData.Setup(d => d.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((User _, BaseItem item) => new UserItemData { Key = item.Id.ToString("N"), LastPlayedDate = byId[item.Id] });
    }

    private static void Scan(ScanFixture fixture)
    {
        using var result = JsonSerializer.SerializeToDocument(fixture.Scan.BackfillUser(ScanFixture.UserId));
        Assert.True(result.RootElement.GetProperty("Success").GetBoolean());
    }
}
