using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

public class AuditLogService
{
    public class Entry
    {
        public DateTimeOffset At { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    private readonly string _path;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private readonly ILogger<AuditLogService> _logger;
    private List<Entry> _entries = new();
    private const int MaxEntries = 5000;

    public AuditLogService(IApplicationPaths applicationPaths, ILogger<AuditLogService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(applicationPaths.PluginConfigurationsPath, "achievementbadges");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "audit.json");
        Load();
    }

    public void Log(string userId, string userName, string type, string details)
    {
        lock (_lock)
        {
            _entries.Add(new Entry
            {
                At = DateTimeOffset.UtcNow,
                UserId = userId ?? string.Empty,
                UserName = userName ?? string.Empty,
                Type = type ?? string.Empty,
                Details = details ?? string.Empty
            });

            if (_entries.Count > MaxEntries)
            {
                _entries = _entries.Skip(_entries.Count - MaxEntries).ToList();
            }

            Save();
        }
    }

    public List<Entry> GetRecent(int limit = 200)
    {
        lock (_lock)
        {
            return _entries.OrderByDescending(e => e.At).Take(limit).ToList();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) { _entries = new(); return; }
            var json = File.ReadAllText(_path);
            _entries = JsonSerializer.Deserialize<List<Entry>>(json, _json) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AchievementBadges] Failed to load audit log.");
            _entries = new();
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, _json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AchievementBadges] Failed to save audit log.");
        }
    }
}
