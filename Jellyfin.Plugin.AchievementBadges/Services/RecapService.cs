using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AchievementBadges.Models;

namespace Jellyfin.Plugin.AchievementBadges.Services;

public class RecapService
{
    private readonly AchievementBadgeService _badgeService;

    public RecapService(AchievementBadgeService badgeService)
    {
        _badgeService = badgeService;
    }

    public object GetRecap(string userId, string period)
    {
        var days = period?.ToLowerInvariant() switch
        {
            "week" => 7,
            "month" => 30,
            "year" => 365,
            _ => 7
        };

        var profile = _badgeService.PeekProfile(userId);
        if (profile is null)
        {
            return new
            {
                Period = period,
                Days = days,
                MoviesWatched = 0,
                EpisodesWatched = 0,
                TotalItems = 0,
                DaysWatched = 0,
                BadgesUnlocked = 0,
                TopGenres = Array.Empty<object>(),
                TopDirectors = Array.Empty<object>(),
                TopActors = Array.Empty<object>()
            };
        }

        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-days));
        var counters = profile.Counters;

        var periodDates = counters.WatchDates
            .Select(d => DateOnly.TryParse(d, out var parsed) ? parsed : default)
            .Where(d => d != default && d >= cutoff)
            .ToHashSet();

        var moviesInPeriod = counters.MoviesByDate
            .Where(kvp => DateOnly.TryParse(kvp.Key, out var d) && d >= cutoff)
            .Sum(kvp => kvp.Value);
        var episodesInPeriod = counters.EpisodesByDate
            .Where(kvp => DateOnly.TryParse(kvp.Key, out var d) && d >= cutoff)
            .Sum(kvp => kvp.Value);

        var badgesUnlocked = profile.Badges
            .Where(b => b.Unlocked && b.UnlockedAt.HasValue && b.UnlockedAt.Value >= DateTimeOffset.Now.AddDays(-days))
            .Select(b => new { b.Id, b.Title, b.Rarity, b.UnlockedAt })
            .OrderByDescending(b => b.UnlockedAt)
            .ToList();

        var topGenres = counters.GenreItemCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => new { Name = kvp.Key, Count = kvp.Value })
            .ToList();

        var topDirectors = counters.DirectorItemCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => new { Name = kvp.Key, Count = kvp.Value })
            .ToList();

        var topActors = counters.ActorItemCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => new { Name = kvp.Key, Count = kvp.Value })
            .ToList();

        return new
        {
            Period = period,
            Days = days,
            MoviesWatched = moviesInPeriod,
            EpisodesWatched = episodesInPeriod,
            TotalItems = moviesInPeriod + episodesInPeriod,
            DaysWatched = periodDates.Count,
            BadgesUnlocked = badgesUnlocked.Count,
            BadgesList = badgesUnlocked,
            TopGenres = topGenres,
            TopDirectors = topDirectors,
            TopActors = topActors
        };
    }
}
