using System.Text.RegularExpressions;

namespace AadhiCrackers.Infrastructure.Services;

/// <summary>
/// Builds object keys. The user-supplied filename is never part of a key: it can carry path
/// traversal, a second extension, unicode that breaks URLs, or simply the name of an object that
/// already exists. A random name sidesteps all of it and, because a re-upload always lands on a
/// fresh key, it is also what makes the one-year immutable cache header safe.
/// </summary>
public static class StorageKeyGenerator
{
    private static readonly Regex UnsafeFolderChars = new("[^a-z0-9-]", RegexOptions.Compiled);

    public static string NewKey(string folder, string extension)
    {
        var safeFolder = SanitizeFolder(folder);
        var safeExtension = SanitizeExtension(extension);
        return $"{safeFolder}/{Guid.NewGuid():N}{safeExtension}";
    }

    /// <summary>
    /// Defence in depth. The API layer already restricts the folder to a fixed allow-list, but the
    /// storage layer must not depend on that having happened: anything that is not a lowercase
    /// letter, digit or hyphen is dropped, so "../../etc" can never become a directory traversal
    /// on the local provider or a surprising prefix in the bucket.
    /// </summary>
    public static string SanitizeFolder(string? folder)
    {
        var value = UnsafeFolderChars.Replace((folder ?? string.Empty).Trim().ToLowerInvariant(), string.Empty);
        return value.Length == 0 ? "misc" : value;
    }

    private static string SanitizeExtension(string? extension)
    {
        var value = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return string.Empty;
        if (!value.StartsWith('.')) value = "." + value;
        return Regex.IsMatch(value, "^\\.[a-z0-9]{1,8}$") ? value : string.Empty;
    }
}

/// <summary>
/// Extension to content type for the formats this API stores. Only used by the legacy
/// <c>SaveFileAsync</c> path; the upload endpoint passes the content type it sniffed from the
/// bytes instead of inferring one.
/// </summary>
public static class StorageContentTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf"
    };

    public static string FromExtension(string? extension) =>
        ByExtension.TryGetValue((extension ?? string.Empty).Trim(), out var contentType)
            ? contentType
            : "application/octet-stream";
}
