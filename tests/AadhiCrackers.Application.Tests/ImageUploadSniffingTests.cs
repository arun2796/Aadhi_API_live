using AadhiCrackers.Application.Common;
using FluentAssertions;
using Xunit;

namespace AadhiCrackers.Application.Tests;

/// <summary>
/// The upload endpoint's only real defence. If these pass, a file that is not one of the four
/// allowed raster formats cannot reach the bucket no matter what the browser called it.
/// </summary>
public class ImageUploadSniffingTests
{
    private static byte[] Header(params byte[] bytes)
    {
        var padded = new byte[ImageContentTypeSniffer.RequiredHeaderBytes];
        bytes.CopyTo(padded, 0);
        return padded;
    }

    private static byte[] Ascii(string text) => text.Select(c => (byte)c).ToArray();

    [Fact]
    public void Jpeg_IsAccepted()
    {
        var accepted = ImageContentTypeSniffer.TrySniff(Header(0xFF, 0xD8, 0xFF, 0xE0), out var contentType, out var extension);

        accepted.Should().BeTrue();
        contentType.Should().Be("image/jpeg");
        extension.Should().Be(".jpg");
    }

    [Fact]
    public void Png_IsAccepted()
    {
        var accepted = ImageContentTypeSniffer.TrySniff(
            Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), out var contentType, out var extension);

        accepted.Should().BeTrue();
        contentType.Should().Be("image/png");
        extension.Should().Be(".png");
    }

    [Fact]
    public void WebP_IsAccepted()
    {
        var bytes = new byte[ImageContentTypeSniffer.RequiredHeaderBytes];
        Ascii("RIFF").CopyTo(bytes, 0);
        Ascii("WEBP").CopyTo(bytes, 8);

        var accepted = ImageContentTypeSniffer.TrySniff(bytes, out var contentType, out var extension);

        accepted.Should().BeTrue();
        contentType.Should().Be("image/webp");
        extension.Should().Be(".webp");
    }

    [Fact]
    public void Avif_IsAccepted()
    {
        var bytes = new byte[ImageContentTypeSniffer.RequiredHeaderBytes];
        bytes[3] = 0x20;                 // ftyp box length
        Ascii("ftyp").CopyTo(bytes, 4);
        Ascii("avif").CopyTo(bytes, 8);

        var accepted = ImageContentTypeSniffer.TrySniff(bytes, out var contentType, out var extension);

        accepted.Should().BeTrue();
        contentType.Should().Be("image/avif");
        extension.Should().Be(".avif");
    }

    [Fact]
    public void TextFileRenamedToJpg_IsRejected()
    {
        // This is the whole point: the client would send filename "photo.jpg" and
        // Content-Type "image/jpeg", and neither is consulted.
        var accepted = ImageContentTypeSniffer.TrySniff(
            Header(Ascii("Definitely not an image, just text.")[..ImageContentTypeSniffer.RequiredHeaderBytes]),
            out _, out _);

        accepted.Should().BeFalse();
    }

    [Fact]
    public void Svg_IsRejected()
    {
        // SVG is a script-bearing document. Serving one back from the image origin would be
        // stored XSS, so it is not on the allow-list even though browsers render it in <img>.
        var bytes = Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/>");

        ImageContentTypeSniffer.TrySniff(bytes.AsSpan(0, ImageContentTypeSniffer.RequiredHeaderBytes), out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void HtmlDocument_IsRejected()
    {
        var bytes = Ascii("<!DOCTYPE html><html><body>hello there</body></html>");

        ImageContentTypeSniffer.TrySniff(bytes.AsSpan(0, ImageContentTypeSniffer.RequiredHeaderBytes), out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Pdf_IsRejected()
    {
        ImageContentTypeSniffer.TrySniff(Header(Ascii("%PDF-1.7")), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Gif_IsRejected()
    {
        // Not in the allow-list the owner agreed (jpeg/png/webp/avif).
        ImageContentTypeSniffer.TrySniff(Header(Ascii("GIF89a")), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void HeicPhoto_IsRejected()
    {
        // Same ISO-BMFF container as AVIF but a brand no browser can decode; it must not slip
        // through the ftyp branch.
        var bytes = new byte[ImageContentTypeSniffer.RequiredHeaderBytes];
        bytes[3] = 0x18;
        Ascii("ftyp").CopyTo(bytes, 4);
        Ascii("heic").CopyTo(bytes, 8);
        Ascii("mif1").CopyTo(bytes, 16);

        ImageContentTypeSniffer.TrySniff(bytes, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void EmptyContent_IsRejected()
    {
        ImageContentTypeSniffer.TrySniff(ReadOnlySpan<byte>.Empty, out _, out _).Should().BeFalse();
    }
}
