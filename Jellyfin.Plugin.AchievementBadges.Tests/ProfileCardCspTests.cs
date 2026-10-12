using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Regression test for issue #157. The shareable profile card is served
/// with <c>script-src 'self'</c> and no <c>'unsafe-inline'</c> (v1.9.7), which
/// blocks inline event handlers as well as inline script blocks. The back
/// button's onclick had been dead since then: every click logged a CSP
/// violation and followed the href. No card template may carry one again.
/// </summary>
public class ProfileCardCspTests
{
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(".Pages.profile-card.html")]
    [InlineData(".Pages.profile-card-blades.html")]
    [InlineData(".Pages.profile-card-metro.html")]
    public void Card_templates_carry_no_inline_event_handlers(string template)
    {
        Assert.DoesNotMatch(new Regex("\\son[a-z]+\\s*=", RegexOptions.IgnoreCase), ReadEmbedded(template));
    }
}
