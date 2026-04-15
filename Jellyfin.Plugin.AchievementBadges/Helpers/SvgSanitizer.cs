using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.AchievementBadges.Helpers;

public static class SvgSanitizer
{
    private const int MaxBytes = 100 * 1024; // 100 KB

    private static readonly string[] DisallowedElements = {
        "script", "foreignObject", "iframe", "embed", "object", "use"
    };

    public static bool TryValidate(string svg, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(svg)) { error = "SVG is empty."; return false; }
        if (Encoding.UTF8.GetByteCount(svg) > MaxBytes) { error = "SVG exceeds 100 KB."; return false; }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                IgnoreComments = false
            };
            using var sr = new StringReader(svg);
            using var reader = XmlReader.Create(sr, settings);
            var doc = XDocument.Load(reader);

            var root = doc.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            {
                error = "Root element must be <svg>.";
                return false;
            }

            foreach (var el in doc.Descendants())
            {
                if (DisallowedElements.Any(d => string.Equals(d, el.Name.LocalName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"Disallowed element <{el.Name.LocalName}>.";
                    return false;
                }
                foreach (var attr in el.Attributes())
                {
                    var name = attr.Name.LocalName;
                    if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Disallowed event handler attribute {name}.";
                        return false;
                    }
                    var value = attr.Value ?? "";
                    var lower = value.Trim().ToLowerInvariant();
                    if (lower.StartsWith("javascript:") || lower.StartsWith("data:text/html"))
                    {
                        error = $"Disallowed URI in attribute {name}.";
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = $"Invalid SVG: {ex.Message}";
            return false;
        }
        return true;
    }
}
