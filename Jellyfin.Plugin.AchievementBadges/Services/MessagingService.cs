using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.AchievementBadges.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

/// <summary>
/// Xbox-style 1:1 messaging between friends.
///
/// Messages are stored in a single JSON file, keyed by a canonical
/// conversation ID (two sorted user IDs joined with '|'). Only users who
/// are mutual friends can exchange messages. Per-sender rate limits and
/// a per-conversation cap keep the store bounded.
///
/// Reads are frequent (the friends drawer polls for unread counts and
/// open threads load the last ~50 messages), writes are much rarer, so
/// the store is held in memory and re-serialized on every mutation.
/// </summary>
public class MessagingService
{
    // ── Limits (conservative; plenty of headroom for real use) ──
    internal const int MaxTextLength                 = 1000;
    internal const int MaxMessagesPerConversation    = 500;  // FIFO trim
    internal const int RateLimitMessagesPerMinute    = 20;   // per sender, per conversation

    private readonly FriendsService _friends;
    private readonly AchievementBadgeService _badgeService;
    private readonly IUserManager _userManager;
    private readonly ILogger<MessagingService> _logger;

    private readonly object _lock = new();
    private readonly string _dataFilePath;
    private MessagingStore _store = new();

    // Per-sender sliding-window rate limiter. Each entry: the timestamps
    // of that sender's sends in the last 60 seconds.
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _rateWindow = new();

    public MessagingService(
        IApplicationPaths applicationPaths,
        FriendsService friends,
        AchievementBadgeService badgeService,
        IUserManager userManager,
        ILogger<MessagingService> logger)
    {
        _friends = friends;
        _badgeService = badgeService;
        _userManager = userManager;
        _logger = logger;

        var pluginDataPath = Path.Combine(applicationPaths.PluginConfigurationsPath, "achievementbadges");
        Directory.CreateDirectory(pluginDataPath);
        _dataFilePath = Path.Combine(pluginDataPath, "messages.json");
        Load();
    }

    // ─────────────────────────────────────────────────────────────────── //
    // Public API
    // ─────────────────────────────────────────────────────────────────── //

    public (bool ok, string? error, Message? message) Send(string fromId, string fromName, string toId, string text)
    {
        fromId = NormalizeId(fromId);
        toId   = NormalizeId(toId);
        text   = (text ?? string.Empty).Trim();

        if (fromId == toId)                     return (false, "Can't message yourself.", null);
        if (string.IsNullOrWhiteSpace(text))    return (false, "Message is empty.", null);
        if (text.Length > MaxTextLength)        return (false, $"Message exceeds {MaxTextLength} character limit.", null);
        if (!_friends.AreMutualFriends(fromId, toId)) return (false, "You can only message friends.", null);

        if (!CheckAndRecordRate(fromId))
            return (false, $"Rate limit: max {RateLimitMessagesPerMinute} messages per minute.", null);

        var msg = new Message
        {
            FromUserId   = fromId,
            FromUserName = string.IsNullOrWhiteSpace(fromName) ? ResolveUserName(fromId) : fromName,
            ToUserId     = toId,
            Text         = text,
            SentAt       = DateTime.UtcNow
        };

        lock (_lock)
        {
            var convId = ConversationId(fromId, toId);
            if (!_store.Conversations.TryGetValue(convId, out var list))
            {
                list = new List<Message>();
                _store.Conversations[convId] = list;
            }
            list.Add(msg);
            // FIFO trim
            if (list.Count > MaxMessagesPerConversation)
            {
                var excess = list.Count - MaxMessagesPerConversation;
                list.RemoveRange(0, excess);
            }
            Save();
        }

        return (true, null, msg);
    }

    /// <summary>
    /// Returns the last N messages between me and the other user, chronological.
    /// Automatically marks inbound messages as read.
    /// </summary>
    public List<Message> GetThread(string meId, string otherId, int limit = 100)
    {
        meId    = NormalizeId(meId);
        otherId = NormalizeId(otherId);
        if (!_friends.AreMutualFriends(meId, otherId)) return new List<Message>();

        lock (_lock)
        {
            var convId = ConversationId(meId, otherId);
            if (!_store.Conversations.TryGetValue(convId, out var list) || list.Count == 0)
                return new List<Message>();

            if (limit <= 0 || limit > list.Count) limit = list.Count;
            var take = list.Skip(list.Count - limit).ToList();

            // Auto-mark-read for inbound messages.
            var changed = false;
            foreach (var m in list)
            {
                if (m.ToUserId == meId && m.ReadAt == null)
                {
                    m.ReadAt = DateTime.UtcNow;
                    changed = true;
                }
            }
            if (changed) Save();

            return take;
        }
    }

    public int GetUnreadCount(string meId)
    {
        meId = NormalizeId(meId);
        lock (_lock)
        {
            return _store.Conversations.Values
                .SelectMany(v => v)
                .Count(m => m.ToUserId == meId && m.ReadAt == null);
        }
    }

    public List<MessageThreadSummary> GetThreads(string meId)
    {
        meId = NormalizeId(meId);
        var summaries = new List<MessageThreadSummary>();
        lock (_lock)
        {
            foreach (var (convId, list) in _store.Conversations)
            {
                if (list.Count == 0) continue;
                // Is this conversation between me and someone?
                var parts = convId.Split('|');
                if (parts.Length != 2) continue;
                if (parts[0] != meId && parts[1] != meId) continue;
                var otherId = parts[0] == meId ? parts[1] : parts[0];

                var last = list[^1];
                summaries.Add(new MessageThreadSummary
                {
                    OtherUserId   = otherId,
                    OtherUserName = ResolveUserName(otherId),
                    LastMessage   = last.Text,
                    LastFromMe    = last.FromUserId == meId,
                    LastAt        = last.SentAt,
                    UnreadCount   = list.Count(m => m.ToUserId == meId && m.ReadAt == null)
                });
            }
        }
        // Newest conversations first
        summaries.Sort((a, b) => b.LastAt.CompareTo(a.LastAt));
        return summaries;
    }

    // ─────────────────────────────────────────────────────────────────── //
    // Internals
    // ─────────────────────────────────────────────────────────────────── //

    private static string ConversationId(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    private bool CheckAndRecordRate(string senderId)
    {
        var now = DateTime.UtcNow;
        var window = _rateWindow.GetOrAdd(senderId, _ => new Queue<DateTime>());
        lock (window)
        {
            // Evict anything older than 60s
            while (window.Count > 0 && (now - window.Peek()).TotalSeconds > 60) window.Dequeue();
            if (window.Count >= RateLimitMessagesPerMinute) return false;
            window.Enqueue(now);
            return true;
        }
    }

    private string ResolveUserName(string userId)
    {
        try
        {
            var user = _userManager.Users.FirstOrDefault(u =>
                u.Id.ToString("N").Equals(userId, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user.Username;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AB] MessagingService: user-name resolve failed for {User}", userId);
        }
        return userId;
    }

    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var s = id.Trim();
        if (Guid.TryParse(s, out var g)) return g.ToString("N");
        return s.ToLowerInvariant();
    }

    // ─────────────────────────────────────────────────────────────────── //
    // Persistence (atomic writes — temp file + File.Move)
    // ─────────────────────────────────────────────────────────────────── //

    private void Load()
    {
        try
        {
            if (!File.Exists(_dataFilePath)) { _store = new MessagingStore(); return; }
            var json = File.ReadAllText(_dataFilePath);
            _store = JsonSerializer.Deserialize<MessagingStore>(json) ?? new MessagingStore();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AB] MessagingService: load failed — starting empty");
            _store = new MessagingStore();
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _dataFilePath + ".tmp";
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(tmp, json);
            File.Move(tmp, _dataFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AB] MessagingService: save failed");
        }
    }
}
