using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

/// <summary>
/// Where unlock toasts appear. The client knows exactly the six placements
/// below.
/// <para>
/// Both the admin's default and a user's own choice arrive as free strings,
/// so they can be empty, misspelt or hand edited. They normalise differently
/// on purpose (#136): the admin default is always a concrete placement, while
/// an empty user value is meaningful and means "follow the server default".
/// An unknown user value therefore falls back to the admin's choice rather
/// than to a hard coded corner.
/// </para>
/// </summary>
public static class ToastPlacement
{
    /// <summary>The default since #74 moved toasts off the subtitle line.</summary>
    public const string TopRight = "top-right";

    /// <summary>Centred under the header (#136).</summary>
    public const string TopCenter = "top-center";

    public const string TopLeft = "top-left";

    public const string BottomRight = "bottom-right";

    /// <summary>The original placement, before #74.</summary>
    public const string BottomCenter = "bottom-center";

    public const string BottomLeft = "bottom-left";

    /// <summary>What a user stores to follow the server default.</summary>
    public const string ServerDefault = "";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        TopRight, TopCenter, TopLeft, BottomRight, BottomCenter, BottomLeft,
    };

    /// <summary>
    /// The admin side: always one of the six placements. Anything empty or
    /// unrecognised becomes <see cref="TopRight"/>, so an upgraded server
    /// keeps showing toasts where it always has.
    /// </summary>
    public static string NormalizeDefault(string? value)
    {
        var v = value?.Trim();
        return v is not null && Known.Contains(v) ? v.ToLowerInvariant() : TopRight;
    }

    /// <summary>
    /// The user side: one of the six placements, or
    /// <see cref="ServerDefault"/>. Anything unrecognised is treated as no
    /// choice, so it follows the admin rather than landing in a corner nobody
    /// picked.
    /// </summary>
    public static string NormalizeUserChoice(string? value)
    {
        var v = value?.Trim();
        return v is not null && Known.Contains(v) ? v.ToLowerInvariant() : ServerDefault;
    }
}
