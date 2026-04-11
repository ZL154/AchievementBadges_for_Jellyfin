using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

public class PlaybackCompletionTracker : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly PlaybackCompletionService _playbackCompletionService;
    private readonly ILogger<PlaybackCompletionTracker> _logger;
    private bool _subscribed;
    private bool _disposed;

    public PlaybackCompletionTracker(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        PlaybackCompletionService playbackCompletionService,
        ILogger<PlaybackCompletionTracker> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _playbackCompletionService = playbackCompletionService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _subscribed = true;
            _logger.LogInformation("[AchievementBadges] PlaybackCompletionTracker started, subscribed to session events.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _subscribed = false;
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        TryRecordCompletion(e, "progress", playedToCompletion: false);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        TryRecordCompletion(e, "stopped", playedToCompletion: e.PlayedToCompletion);
    }

    private void TryRecordCompletion(PlaybackProgressEventArgs e, string source, bool playedToCompletion)
    {
        try
        {
            var userGuid = ResolveUserId(e);
            if (userGuid == Guid.Empty)
            {
                _logger.LogDebug("[AchievementBadges] {Source} event: no user id resolved.", source);
                return;
            }

            var userId = userGuid.ToString("D");
            var item = e.Item;
            if (item is null)
            {
                _logger.LogDebug("[AchievementBadges] {Source} event for user {UserId}: null Item.", source, userId);
                return;
            }

            var itemId = item.Id.ToString("D");
            var itemType = item.GetType().Name;
            var isMovie = string.Equals(itemType, "Movie", StringComparison.OrdinalIgnoreCase);
            var isEpisode = string.Equals(itemType, "Episode", StringComparison.OrdinalIgnoreCase);

            if (!isMovie && !isEpisode)
            {
                return;
            }

            var runTimeTicks = item.RunTimeTicks ?? 0;
            var positionTicks = e.PlaybackPositionTicks ?? 0;

            double completionPercent;
            if (playedToCompletion)
            {
                completionPercent = 100d;
            }
            else if (runTimeTicks > 0 && positionTicks > 0)
            {
                completionPercent = (double)positionTicks / runTimeTicks * 100d;
            }
            else
            {
                _logger.LogDebug(
                    "[AchievementBadges] {Source} event for user {UserId} item {ItemId}: runtime={RunTime} position={Position}.",
                    source, userId, itemId, runTimeTicks, positionTicks);
                return;
            }

            var libraryName = ResolveLibraryName(item);

            var success = _playbackCompletionService.RecordCompletion(
                userId: userId,
                itemId: itemId,
                isMovie: isMovie,
                isEpisode: isEpisode,
                isSeriesCompleted: false,
                completionPercent: completionPercent,
                playedAt: DateTimeOffset.Now,
                libraryName: libraryName,
                message: out var message);

            if (success)
            {
                _logger.LogInformation(
                    "[AchievementBadges] Recorded playback from {Source} user={UserId} item={ItemId} library={Library} completion={Completion:0.##}%",
                    source, userId, itemId, libraryName ?? "(none)", completionPercent);
            }
            else
            {
                _logger.LogDebug(
                    "[AchievementBadges] Skipped playback from {Source} user={UserId} item={ItemId} reason={Reason}",
                    source, userId, itemId, message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AchievementBadges] Failed to process playback completion from {Source}.", source);
        }
    }

    private static Guid ResolveUserId(PlaybackProgressEventArgs e)
    {
        if (e.Session is { UserId: var sessionUserId } && sessionUserId != Guid.Empty)
        {
            return sessionUserId;
        }

        if (e.Users is { Count: > 0 })
        {
            var first = e.Users[0];
            if (first is not null && first.Id != Guid.Empty)
            {
                return first.Id;
            }
        }

        return Guid.Empty;
    }

    private string? ResolveLibraryName(BaseItem item)
    {
        try
        {
            var folders = _libraryManager.GetCollectionFolders(item);
            var name = folders?.FirstOrDefault()?.Name;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AchievementBadges] Failed to resolve collection folder for item {ItemId}.", item.Id);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unsubscribe();
        _disposed = true;
    }
}
