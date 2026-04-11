using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.AchievementBadges.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AchievementBadges.Services;

public class WebhookNotifier
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ILogger<WebhookNotifier> _logger;

    public WebhookNotifier(ILogger<WebhookNotifier> logger)
    {
        _logger = logger;
    }

    public void NotifyUnlock(string userName, AchievementBadge badge)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.WebhookEnabled || string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            return;
        }

        var template = string.IsNullOrWhiteSpace(config.WebhookMessageTemplate)
            ? "{user} unlocked {badge}"
            : config.WebhookMessageTemplate!;

        var content = template
            .Replace("{user}", userName ?? "Someone", StringComparison.OrdinalIgnoreCase)
            .Replace("{badge}", badge.Title ?? "a badge", StringComparison.OrdinalIgnoreCase)
            .Replace("{rarity}", badge.Rarity ?? "Common", StringComparison.OrdinalIgnoreCase)
            .Replace("{description}", badge.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var payload = new { content };
        var url = config.WebhookUrl!;

        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using var res = await _http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AchievementBadges] Webhook POST returned {Status}", (int)res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AchievementBadges] Webhook POST failed.");
            }
        });
    }
}
