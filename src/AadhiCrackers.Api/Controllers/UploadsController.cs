using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

/// <summary>
/// The one place the admin UI turns a file the shop owner picked into a permanent image URL.
///
/// THREAT MODEL. Every other endpoint takes JSON that a validator has already shaped; this one
/// takes arbitrary bytes from a browser and puts them on an origin that will later serve them back
/// to customers. So nothing the client says is trusted: not the Content-Type header, not the
/// filename, not the extension. The bytes are sniffed, the key is generated here, and the size is
/// capped before a single byte reaches the object store.
/// </summary>
[ApiController]
[Route("api/v1/uploads")]
public class UploadsController : ControllerBase
{
    /// <summary>
    /// 8 MB. The admin UI resizes client-side before upload, so a real product photo lands well
    /// under this; the cap exists to stop a mistake (someone picking a 40 MB camera original) or an
    /// abuse of the endpoint from filling the bucket.
    /// </summary>
    public const long MaxFileSizeBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Headroom over <see cref="MaxFileSizeBytes"/> for multipart framing, so a file that is only
    /// slightly too large is rejected by OUR check with a clear 400 rather than by Kestrel with a
    /// bare 413 the UI cannot explain.
    /// </summary>
    private const long MaxRequestBodyBytes = 12 * 1024 * 1024;

    /// <summary>
    /// Closed set of key prefixes. An open folder parameter would let a caller scatter objects
    /// across the bucket (or, on the local provider, walk out of wwwroot), and it would make the
    /// bucket impossible to reason about later.
    /// </summary>
    private static readonly HashSet<string> AllowedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "products", "categories", "brands", "banners", "combos", "settings", "payment-proofs"
    };

    private const string DefaultFolder = "products";

    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(
        IFileStorageService fileStorage,
        ICurrentUserService currentUser,
        ILogger<UploadsController> logger)
    {
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Stores one image and returns its permanent public URL.
    /// multipart/form-data with field "file" and optional field "folder".
    /// </summary>
    [HttpPost("image")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminUpload)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBodyBytes)]
    [ProducesResponseType(typeof(UploadImageApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UploadImageResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UploadImageApiResponse>> UploadImage(
        [FromForm] UploadImageForm form,
        CancellationToken cancellationToken)
    {
        var file = form.File;
        var folder = form.Folder;

        if (file == null || file.Length == 0)
        {
            return Bad("No file was uploaded. Send the image as multipart/form-data in a field named 'file'.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return Bad($"The image is {FormatSize(file.Length)}, which is over the {FormatSize(MaxFileSizeBytes)} limit. Resize or re-compress it and try again.");
        }

        var requestedFolder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder.Trim();
        if (!AllowedFolders.Contains(requestedFolder))
        {
            return Bad($"'{requestedFolder}' is not a valid folder. Allowed folders: {string.Join(", ", AllowedFolders.Order(StringComparer.Ordinal))}.");
        }

        // Buffered whole because the size is already bounded at 8 MB and because both the sniff and
        // the S3 PUT want a seekable stream with a known length — a non-seekable body would force
        // the SDK into chunked signing, which is exactly the path R2 is fussiest about.
        using var buffer = new MemoryStream((int)file.Length);
        await using (var upload = file.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, cancellationToken);
        }

        // Re-check against what actually arrived, not the declared length.
        if (buffer.Length > MaxFileSizeBytes)
        {
            return Bad($"The image is {FormatSize(buffer.Length)}, which is over the {FormatSize(MaxFileSizeBytes)} limit. Resize or re-compress it and try again.");
        }

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageContentTypeSniffer.RequiredHeaderBytes));
        if (!ImageContentTypeSniffer.TrySniff(header, out var contentType, out var extension))
        {
            _logger.LogWarning(
                "Rejected upload from {User}: content does not match any allowed image format (client claimed {ClaimedContentType}, filename {FileName})",
                _currentUser.Email ?? _currentUser.UserId, file.ContentType, file.FileName);

            return Bad(
                $"This file is not a {ImageContentTypeSniffer.AllowedFormatsDescription} image. " +
                "The check reads the file's own contents, so renaming a file to .jpg will not pass it.");
        }

        buffer.Position = 0;
        var stored = await _fileStorage.SaveObjectAsync(buffer, requestedFolder, contentType, extension, cancellationToken);

        _logger.LogInformation(
            "{User} uploaded {SizeBytes} bytes of {ContentType} to {Key}",
            _currentUser.Email ?? _currentUser.UserId, stored.SizeBytes, stored.ContentType, stored.Key);

        var payload = new UploadImageResponse
        {
            Url = stored.Url,
            Key = stored.Key,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes
        };

        return Ok(UploadImageApiResponse.From(payload, "Image uploaded successfully", _currentUser.CorrelationId));
    }

    private BadRequestObjectResult Bad(string message) =>
        BadRequest(ApiResponse<UploadImageResponse>.Fail(message, _currentUser.CorrelationId));

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (double)(1024 * 1024):0.#} MB"
            : $"{bytes / 1024d:0.#} KB";
}

/// <summary>
/// The multipart body: field "file" (required) and field "folder" (optional). Binding is
/// case-insensitive, so the lowercase field names the UI sends map onto these properties.
///
/// The two fields are a MODEL rather than two loose [FromForm] parameters because Swashbuckle
/// refuses to describe an action that puts [FromForm] directly on an IFormFile and fails the whole
/// /swagger/v1/swagger.json document, not just this operation — which would take the API's only
/// interactive documentation down with it.
/// </summary>
public sealed class UploadImageForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    /// <summary>One of: products, categories, brands, banners, combos, settings, payment-proofs. Defaults to products.</summary>
    [FromForm(Name = "folder")]
    public string? Folder { get; set; }
}
