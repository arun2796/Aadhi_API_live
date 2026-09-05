using System.Text.RegularExpressions;

namespace AadhiCrackers.Application.Common;

/// <summary>
/// Converts Google Drive "share" links (which return an HTML page) into
/// direct-image URLs that work inside &lt;img&gt; tags. Any other URL is
/// returned unchanged. Handled forms:
///   https://drive.google.com/file/d/{id}/view?usp=sharing
///   https://drive.google.com/open?id={id}
///   https://drive.google.com/uc?export=view&amp;id={id}
/// </summary>
public static partial class ImageUrlNormalizer
{
    [GeneratedRegex(@"drive\.google\.com/file/d/([A-Za-z0-9_-]{10,})", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathPattern();

    [GeneratedRegex(@"drive\.google\.com/(?:open|uc|thumbnail)[^""\s]*?[?&]id=([A-Za-z0-9_-]{10,})", RegexOptions.IgnoreCase)]
    private static partial Regex QueryIdPattern();

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        var trimmed = url.Trim();
        if (!trimmed.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase)) return trimmed;

        // Already the direct thumbnail form — keep as-is.
        if (trimmed.Contains("drive.google.com/thumbnail", StringComparison.OrdinalIgnoreCase)) return trimmed;

        var match = FilePathPattern().Match(trimmed);
        if (!match.Success) match = QueryIdPattern().Match(trimmed);
        if (!match.Success) return trimmed;

        return $"https://drive.google.com/thumbnail?id={match.Groups[1].Value}&sz=w1000";
    }
}
