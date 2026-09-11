using AadhiCrackers.Contracts.Common;

namespace AadhiCrackers.Contracts.Storage;

/// <summary>
/// What POST /api/v1/uploads/image returns on success.
///
/// <see cref="Url"/> is what goes straight into a product's imageUrl: absolute and permanent when
/// the R2 provider is active, site-relative ("/storage/...") in local development. It is stable
/// forever — a replacement image is a new upload with a new URL — which is what lets the object be
/// served with a one-year immutable cache header.
/// </summary>
public class UploadImageResponse
{
    public string Url { get; set; } = string.Empty;

    /// <summary>The object key inside the bucket, e.g. "products/9f1c….jpg". Keep it if you ever want to delete the object.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The content type SNIFFED from the bytes, not the one the browser claimed.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

/// <summary>
/// The success body of POST /api/v1/uploads/image.
///
/// It carries the four upload fields TWICE, and that is deliberate. Every other endpoint in this
/// API answers with the ApiResponse envelope { success, message, data, correlationId } and both
/// front-ends are written against it (they read res.data.data and pull correlationId out of the
/// body for error toasts), so breaking the envelope here would make this the one endpoint the
/// shared client cannot handle. The agreed contract for this endpoint, however, was written as a
/// flat { url, key, contentType, sizeBytes }. Rather than pick one and break the other, the four
/// fields are also projected onto the top level: res.data.url and res.data.data.url both work, and
/// no caller has to know which convention it was written against.
/// </summary>
public class UploadImageApiResponse : ApiResponse<UploadImageResponse>
{
    public string Url => Data?.Url ?? string.Empty;
    public string Key => Data?.Key ?? string.Empty;
    public string ContentType => Data?.ContentType ?? string.Empty;
    public long SizeBytes => Data?.SizeBytes ?? 0;

    public static UploadImageApiResponse From(UploadImageResponse data, string? message, string? correlationId) =>
        new() { Success = true, Data = data, Message = message, CorrelationId = correlationId };
}
