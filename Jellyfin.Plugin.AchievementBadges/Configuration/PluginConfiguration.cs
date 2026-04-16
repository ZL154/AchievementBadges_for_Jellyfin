using System.Collections.Generic;
using Jellyfin.Plugin.AchievementBadges.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AchievementBadges.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    public bool ShowOnUserHome { get; set; } = true;

    public bool EnableDebugEndpoints { get; set; } = true;

    public int MinimumPlaySecondsForCompletion { get; set; } = 300;

    public List<string> DisabledBadgeIds { get; set; } = new();

    public List<AchievementDefinition> CustomBadges { get; set; } = new();

    public List<AchievementDefinition> Challenges { get; set; } = new();

    public string? WebhookUrl { get; set; }

    public bool WebhookEnabled { get; set; }

    public string WebhookMessageTemplate { get; set; }
        = "🏆 **{user}** unlocked **{badge}** ({rarity}) — {description}";

    public bool EnableUnlockToasts { get; set; } = true;

    public bool EnableHomeWidget { get; set; } = false;

    public bool EnableItemDetailRibbon { get; set; } = false;

    // Feature kill switches
    public bool LeaderboardEnabled { get; set; } = true;
    public bool CompareEnabled { get; set; } = true;
    public bool ActivityFeedEnabled { get; set; } = true;
    public bool PrestigeEnabled { get; set; } = true;
    public bool QuestsEnabled { get; set; } = true;
    public bool ForcePrivacyMode { get; set; } = false;
    public bool ForceSpoilerMode { get; set; } = false;
    public bool ForceExtremeSpoilerMode { get; set; } = false;

    // Badge controls
    public int MaxEquippedBadges { get; set; } = 5;
    public bool RestrictBadgeVisibility { get; set; } = false;
    public List<string> DisabledBadgeCategories { get; set; } = new();
    public string WelcomeMessage { get; set; } = "";

    // Default UI language when a user hasn't picked one. Supported:
    // "en", "fr", "es", "de", "it", "pt", "zh", "ja".
    public string DefaultLanguage { get; set; } = "en";

    // Admin-supplied SVG used to replace the Xbox logo in the toast animation.
    // Stored as a base64-encoded SVG string (no data:-URI prefix). Empty string
    // means "use the default Xbox logo bundled with the plugin".
    public string CustomXboxLogoSvg { get; set; } = "";

    // When true, audit log entries store only the UserId GUID, not UserName.
    // The admin endpoint returns "[redacted]" for the UserName of any entry
    // that was stored with redaction on.
    public bool RedactUsernamesInAuditLog { get; set; } = false;

    // Admin force-override for the equipped-badge showcase UI (sidebar strip,
    // header dots, profile-card equipped slots). When true, these elements are
    // hidden for all users regardless of per-user preferences. Default false.
    public bool ForceHideEquippedShowcase { get; set; } = false;
}
