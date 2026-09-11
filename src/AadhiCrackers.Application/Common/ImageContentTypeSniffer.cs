

namespace AadhiCrackers.Application.Common;

/// <summary>
/// Decides what an uploaded file ACTUALLY is by reading its leading bytes.
///
/// The browser-supplied Content-Type and the filename extension are both attacker-controlled and
/// are deliberately ignored here: a .jpg that is really an HTML page or an SVG is the classic way
/// a "product image" upload turns into stored XSS on whatever origin serves the bucket. Only the
/// four raster formats the storefront actually renders are accepted, and the extension that ends
/// up in the object key is derived from the sniffed type — never from the upload.
///
/// SVG is intentionally NOT allowed: it is a script-bearing document, not a raster image.
/// </summary>
public static class ImageContentTypeSniffer
{
    /// <summary>Bytes needed before a verdict can be reached (AVIF needs the ftyp brand at 8..11).</summary>
    public const int RequiredHeaderBytes = 32;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Avif = "image/avif";

    /// <summary>The content types this API will store, in the order they are reported to the UI.</summary>
    public static readonly IReadOnlyList<string> AllowedContentTypes = new[] { Jpeg, Png, WebP, Avif };

    /// <summary>
    /// Returns the sniffed content type and the canonical extension for it, or false when the bytes
    /// are not one of the allowed image formats.
    /// </summary>
    public static bool TrySniff(ReadOnlySpan<byte> header, out string contentType, out string extension)
    {
        contentType = string.Empty;
        extension = string.Empty;

        // JPEG — SOI marker FF D8 FF (JFIF/Exif/raw all share it).
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            contentType = Jpeg;
            extension = ".jpg";
            return true;
        }

        // PNG — the 8-byte signature, including the CR/LF/EOF bytes that catch text-mode corruption.
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            contentType = Png;
            extension = ".png";
            return true;
        }

        // WebP — RIFF container: "RIFF" <4-byte size> "WEBP".
        if (header.Length >= 12 &&
            AsciiEquals(header.Slice(0, 4), "RIFF") &&
            AsciiEquals(header.Slice(8, 4), "WEBP"))
        {
            contentType = WebP;
            extension = ".webp";
            return true;
        }

        // AVIF — ISO-BMFF: <4-byte box size> "ftyp" <major brand> <minor version> <compatible brands...>.
        // The major brand of an AVIF written by an encoder is usually "avif" (or "avis" for
        // sequences), but some encoders put a different major brand and list "avif" only among the
        // compatible brands, so the whole ftyp box (bounded by what we read) is scanned.
        if (header.Length >= 12 && AsciiEquals(header.Slice(4, 4), "ftyp"))
        {
            // Stay inside the ftyp box so brand-looking bytes from later boxes can never vote.
            var boxSize = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            var limit = boxSize is > 8 and <= 4096 ? Math.Min(boxSize, header.Length) : header.Length;

            for (var offset = 8; offset + 4 <= limit; offset += 4)
            {
                var brand = header.Slice(offset, 4);
                if (AsciiEquals(brand, "avif") || AsciiEquals(brand, "avis"))
                {
                    contentType = Avif;
                    extension = ".avif";
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string expected)
    {
        if (bytes.Length != expected.Length) return false;
        for (var i = 0; i < expected.Length; i++)
        {
            if (bytes[i] != (byte)expected[i]) return false;
        }
        return true;
    }

    /// <summary>Human-readable allow-list, used verbatim in the 400 responses the UI shows.</summary>
    public const string AllowedFormatsDescription = "JPEG, PNG, WebP or AVIF";
}
