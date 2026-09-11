using System.Net;
using AadhiCrackers.Application.Common;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Infrastructure.Configuration;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.Services;

/// <summary>
/// Stores uploads in Cloudflare R2 over its S3-compatible API.
///
/// WHY R2 AND NOT GOOGLE DRIVE. Drive share links answer every image request with a 302 to a
/// one-off host, `Cache-Control: no-cache, no-store, max-age=0`, `Content-Type: application/binary`
/// and a tracking cookie — nothing in the chain (browser, Cloudflare, an image CDN) is allowed to
/// keep a copy, so 180 products means 180 uncached round trips per page view, and Drive's download
/// quota starts returning 403 on exactly the files that are most popular. Objects written here are
/// stamped immutable for a year, so they are fetched once and then served from cache forever.
///
/// CACHE INVALIDATION IS NOT A PROBLEM because keys are random per upload: changing a product's
/// photo produces a NEW key and therefore a new URL, and the old object simply stops being
/// referenced. Nothing ever needs to be purged, which is what makes the one-year immutable header
/// safe to set unconditionally.
/// </summary>
public sealed class R2FileStorageService : IFileStorageService, IDisposable
{
    /// <summary>One year, immutable. See the class remarks on why this is safe here.</summary>
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Hard ceiling on one R2 call, retries included. The admin UI's HTTP client gives up at 30
    /// seconds; if the server were allowed to run past that, a misconfigured bucket would surface
    /// in the browser as a bare network error with no explanation, which is exactly the failure
    /// mode this whole class is written to avoid. Answering at 25 seconds means our own message —
    /// naming the configuration key at fault — always gets there first.
    /// </summary>
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(25);

    private readonly R2Options _options;
    private readonly ILogger<R2FileStorageService> _logger;
    private readonly IAmazonS3 _client;

    public R2FileStorageService(StorageOptions storageOptions, ILogger<R2FileStorageService> logger)
    {
        _options = storageOptions.R2;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,

            // R2 addresses buckets as <endpoint>/<bucket>; virtual-host style would resolve to a
            // hostname that does not exist.
            ForcePathStyle = true,

            // R2 has no regions but SigV4 must still sign against something; "auto" is what
            // Cloudflare documents.
            AuthenticationRegion = "auto",

            // Bounded failure. Without these a wrong endpoint or a network black hole would leave
            // the admin's upload request hanging until the browser gave up, with nothing logged.
            // One retry covers a transient blip; the per-attempt timeout plus OperationBudget below
            // keep the total inside the admin UI's own 30 second HTTP timeout, so the admin always
            // sees OUR error message rather than a client-side "network error".
            Timeout = TimeSpan.FromSeconds(20),
            MaxErrorRetry = 1,

            // AWS SDK v4 defaults to adding a CRC32 trailer via aws-chunked encoding. R2's
            // compatibility with that has been uneven, and the failure it produces is an opaque
            // signature error, so the checksum is only sent when the operation actually requires
            // one. HTTPS plus SigV4 payload signing still cover integrity.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        _client = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey), config);

        _logger.LogInformation(
            "File storage provider: Cloudflare R2. Endpoint {Endpoint}, bucket {Bucket}, public base URL {PublicBaseUrl}",
            _options.ServiceUrl, _options.BucketName, _options.PublicBaseUrl);
    }

    public async Task<StoredFileResult> SaveObjectAsync(
        Stream fileStream,
        string folder,
        string contentType,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var key = StorageKeyGenerator.NewKey(folder, extension);
        var sizeBytes = fileStream.CanSeek ? fileStream.Length - fileStream.Position : -1;

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            AutoCloseStream = false,

            // Plain (non aws-chunked) body: the stream is a bounded in-memory buffer, so the SDK
            // can sign the real payload hash, and R2 gets an ordinary Content-Length upload.
            UseChunkEncoding = false
        };

        request.Headers.CacheControl = ImmutableCacheControl;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(OperationBudget);

        try
        {
            await _client.PutObjectAsync(request, budget.Token);
        }
        catch (Exception ex)
        {
            throw Translate(ex, $"upload object '{key}'", budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
        }

        var url = $"{_options.PublicBaseUrl}/{key}";
        _logger.LogInformation("Stored {SizeBytes} byte {ContentType} in R2 as {Key}", sizeBytes, contentType, key);

        return new StoredFileResult(url, key, contentType, sizeBytes);
    }

    public async Task<string> SaveFileAsync(
        Stream fileStream,
        string fileName,
        string folder = "products",
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        var contentType = StorageContentTypes.FromExtension(extension);
        var result = await SaveObjectAsync(fileStream, folder, contentType, extension, cancellationToken);
        return result.Url;
    }

    /// <summary>
    /// Accepts either a bare key ("products/ab12.jpg") or a full public URL, so a caller holding
    /// only the stored image URL can still delete the object.
    /// </summary>
    public async Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var key = ToKey(relativePath);
        if (string.IsNullOrWhiteSpace(key)) return false;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(OperationBudget);

        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            }, budget.Token);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            throw Translate(ex, $"delete object '{key}'", budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
        }
    }

    private string ToKey(string relativePathOrUrl)
    {
        var value = (relativePathOrUrl ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;

        if (value.StartsWith(_options.PublicBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            value = value[_options.PublicBaseUrl.Length..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            value = absolute.AbsolutePath;
        }

        return value.TrimStart('/');
    }

    /// <summary>
    /// Turns an SDK failure into something the shop owner can act on. Every branch names the
    /// configuration key most likely to be at fault, because the alternative — an
    /// AmazonS3Exception surfacing as a bare 500 — tells them nothing about which of the five R2
    /// values they got wrong.
    /// </summary>
    private FileStorageException Translate(Exception ex, string operation, bool budgetExpired = false)
    {
        _logger.LogError(ex, "Cloudflare R2 failure while trying to {Operation} in bucket {Bucket} at {Endpoint}",
            operation, _options.BucketName, _options.ServiceUrl);

        // Our own deadline, not the caller hanging up: report it as a timeout rather than letting
        // it fall through to the generic "cancelled" branch.
        if (budgetExpired)
        {
            return new FileStorageException(
                $"Cloudflare R2 did not respond within {OperationBudget.TotalSeconds:0} seconds while trying to {operation} " +
                $"at {_options.ServiceUrl}. Check Storage:R2:AccountId and that the server can reach Cloudflare over HTTPS.", ex);
        }

        switch (ex)
        {
            case AmazonS3Exception s3 when s3.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized:
                return new FileStorageException(
                    $"Cloudflare R2 rejected the credentials ({(int)s3.StatusCode} {s3.ErrorCode}). Check Storage:R2:AccessKeyId and " +
                    "Storage:R2:SecretAccessKey, and that the R2 API token has Object Read & Write on bucket " +
                    $"'{_options.BucketName}'.", ex);

            case AmazonS3Exception s3 when s3.StatusCode == HttpStatusCode.NotFound || s3.ErrorCode == "NoSuchBucket":
                return new FileStorageException(
                    $"Cloudflare R2 has no bucket named '{_options.BucketName}' on account '{_options.AccountId}'. " +
                    "Check Storage:R2:BucketName and Storage:R2:AccountId.", ex);

            case AmazonS3Exception s3:
                return new FileStorageException(
                    $"Cloudflare R2 refused to {operation}: {(int?)s3.StatusCode} {s3.ErrorCode} — {s3.Message}", ex);

            // Transport-level failure. A wrong AccountId is the usual cause and shows up here
            // rather than as an S3 error, because the endpoint hostname is built from it: an
            // account that does not exist fails DNS or the TLS handshake before R2 ever sees a
            // request, so the SDK never gets an S3 error code back to report.
            case AmazonServiceException { InnerException: HttpRequestException } or HttpRequestException:
                return new FileStorageException(
                    $"Could not reach Cloudflare R2 at {_options.ServiceUrl} to {operation}: {InnermostMessage(ex)}. " +
                    "Check Storage:R2:AccountId — the endpoint hostname is built from it, so a wrong account id fails here — " +
                    "and confirm the server has outbound HTTPS access.", ex);

            case AmazonServiceException service:
                return new FileStorageException(
                    $"Could not reach Cloudflare R2 at {_options.ServiceUrl} to {operation}: {service.Message}. " +
                    "Check Storage:R2:AccountId (the endpoint is derived from it) and outbound network access.", ex);

            case OperationCanceledException:
                return new FileStorageException(
                    $"The request to Cloudflare R2 to {operation} timed out or was cancelled after 30 seconds.", ex);

            default:
                return new FileStorageException(
                    $"Unexpected failure talking to Cloudflare R2 while trying to {operation}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// TLS and DNS failures nest the useful sentence two or three levels down; the outer message is
    /// always the useless "see inner exception".
    /// </summary>
    private static string InnermostMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null) current = current.InnerException;
        return current.Message.TrimEnd('.');
    }

    public void Dispose() => _client.Dispose();
}
