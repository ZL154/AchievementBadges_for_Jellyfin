using System.Linq;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

/// <summary>
/// [issue #155] The server root, the base URL Jellyfin runs under, as a URL
/// relative to a page the plugin serves itself: the profile card and the
/// standalone page.
/// <para>
/// The browser resolves a relative URL against the address it loaded the page
/// from, so the result keeps whatever prefix sits in front of Jellyfin, be it
/// the base URL (https://example.com/jellyfin) or a reverse proxy that
/// publishes Jellyfin under another path. A root-relative /Users/... or
/// /web/... dropped that prefix, and a proxy that forwards only the prefix
/// answered 404.
/// </para>
/// </summary>
public static class ServerRoot
{
    /// <summary>
    /// One <c>../</c> per folder between the page and the server root, or
    /// <c>./</c> for a page in the root itself.
    /// </summary>
    /// <param name="requestPath">The page's path below the base URL, as
    /// <c>HttpRequest.Path</c> carries it. Routing answers the same page with
    /// a trailing slash too, and the browser then resolves from one folder
    /// deeper, so that slash counts like any other.</param>
    public static string RelativeFrom(string? requestPath)
    {
        var depth = (requestPath ?? string.Empty).Count(c => c == '/') - 1;
        return depth > 0 ? string.Concat(Enumerable.Repeat("../", depth)) : "./";
    }
}
