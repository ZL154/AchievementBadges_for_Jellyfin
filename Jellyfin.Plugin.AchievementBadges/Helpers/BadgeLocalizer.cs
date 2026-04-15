using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.AchievementBadges.Models;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

/// <summary>
/// Loads per-language badge title/description maps from the embedded
/// <c>Pages/translations/badges.{lang}.json</c> files and applies them to a
/// badge clone at request time.
///
/// Missing languages and missing per-badge entries fall back to the English
/// text that's already in the badge (from <see cref="AchievementDefinitions"/>),
/// so calling <see cref="Localize"/> with an unknown lang is always safe and
/// never drops a badge's title/description.
/// </summary>
public static class BadgeLocalizer
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, Entry>?> _cache = new();

    private static readonly HashSet<string> _supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "fr", "es", "de", "it", "pt", "zh", "ja"
    };

    private sealed class Entry
    {
        public string? title { get; set; }
        public string? description { get; set; }
    }

    private static string NormalizeLang(string? lang)
    {
        var l = (lang ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(l) || l == "default")
        {
            l = Plugin.Instance?.Configuration?.DefaultLanguage ?? "en";
            l = l.ToLowerInvariant();
        }
        if (!_supported.Contains(l)) l = "en";
        return l;
    }

    private static Dictionary<string, Entry>? LoadFor(string lang)
    {
        return _cache.GetOrAdd(lang, static key =>
        {
            var content = ResourceReader.ReadEmbeddedText(
                "Jellyfin.Plugin.AchievementBadges.Pages.translations.badges." + key + ".json");
            if (string.IsNullOrWhiteSpace(content)) return null;
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Mutates the badge in-place, replacing Title and Description with the
    /// localized text (if one is available for the requested language).
    /// English is the fallback and is effectively a no-op (the badge already
    /// carries English text from the definitions).
    /// </summary>
    public static void Localize(AchievementBadge badge, string? lang)
    {
        if (badge == null) return;
        var l = NormalizeLang(lang);
        if (l == "en") return;
        var dict = LoadFor(l);
        if (dict == null) return;
        if (!dict.TryGetValue(badge.Id, out var entry) || entry == null) return;
        if (!string.IsNullOrWhiteSpace(entry.title)) badge.Title = entry.title!;
        if (!string.IsNullOrWhiteSpace(entry.description)) badge.Description = entry.description!;
    }
}
