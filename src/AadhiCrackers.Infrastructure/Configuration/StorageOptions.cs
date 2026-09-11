using Microsoft.Extensions.Configuration;

namespace AadhiCrackers.Infrastructure.Configuration;

/// <summary>
/// Binds the "Storage" configuration section. Everything here arrives from configuration —
/// appsettings.json for local development, environment variables (Storage__Provider,
/// Storage__R2__AccessKeyId, ...) on the server. NOTHING is hardcoded and no default credential
/// exists: an unset R2 deployment fails at startup rather than silently writing to disk.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string ProviderLocal = "Local";
    public const string ProviderR2 = "R2";

    /// <summary>"Local" (default) or "R2".</summary>
    public string Provider { get; set; } = ProviderLocal;

    public R2Options R2 { get; set; } = new();

    public bool UsesR2 => string.Equals(Provider?.Trim(), ProviderR2, StringComparison.OrdinalIgnoreCase);
    public bool UsesLocal => string.Equals(Provider?.Trim(), ProviderLocal, StringComparison.OrdinalIgnoreCase);
}

public sealed class R2Options
{
    /// <summary>Cloudflare account id; the S3 endpoint is derived from it.</summary>
    public string AccountId { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// The origin objects are SERVED from — the r2.dev subdomain or (better) a custom domain in
    /// front of the bucket. This is not the S3 API endpoint; uploads go to the API endpoint while
    /// the public URL handed back to the browser is built from this value.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The S3-compatible API endpoint R2 exposes for this account.</summary>
    public string ServiceUrl => $"https://{AccountId.Trim()}.r2.cloudflarestorage.com";
}

/// <summary>
/// Startup validation for the storage section. Deliberately loud: a shop that sets
/// Storage:Provider=R2 and mistypes one key must find out when the service starts, not weeks
/// later when every product image it uploaded turns out to have been written to a local disk
/// that the next release replaces.
/// </summary>
public static class StorageOptionsValidator
{
    /// <summary>
    /// Binds and validates. Throws <see cref="InvalidOperationException"/> naming the exact
    /// configuration keys at fault.
    /// </summary>
    public static StorageOptions BindAndValidate(IConfiguration configuration)
    {
        var options = new StorageOptions();
        configuration.GetSection(StorageOptions.SectionName).Bind(options);

        // Unset / blank keeps the historical local-disk behaviour, so nothing breaks when the key
        // is simply absent.
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            options.Provider = StorageOptions.ProviderLocal;
        }

        if (options.UsesLocal)
        {
            return options;
        }

        if (!options.UsesR2)
        {
            throw new InvalidOperationException(
                $"Storage:Provider (env Storage__Provider) is '{options.Provider}', which is not a known storage provider. " +
                $"Set it to '{StorageOptions.ProviderR2}' or '{StorageOptions.ProviderLocal}', or leave it unset to use '{StorageOptions.ProviderLocal}'.");
        }

        var missing = new List<string>();
        void Require(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value)) missing.Add($"{key} (env {key.Replace(":", "__")})");
        }

        Require(options.R2.AccountId, "Storage:R2:AccountId");
        Require(options.R2.AccessKeyId, "Storage:R2:AccessKeyId");
        Require(options.R2.SecretAccessKey, "Storage:R2:SecretAccessKey");
        Require(options.R2.BucketName, "Storage:R2:BucketName");
        Require(options.R2.PublicBaseUrl, "Storage:R2:PublicBaseUrl");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Storage:Provider is set to 'R2' but required Cloudflare R2 configuration is missing: " +
                string.Join(", ", missing) + ". " +
                "Set every value (in production these are environment variables) or set Storage__Provider=Local to fall back to local disk. " +
                "Startup is aborted deliberately rather than silently storing uploads on the server's own disk, where a release replacement loses them.");
        }

        var publicBaseUrl = options.R2.PublicBaseUrl.Trim();
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"Storage:R2:PublicBaseUrl (env Storage__R2__PublicBaseUrl) must be an absolute http(s) URL such as " +
                $"'https://images.aadhicracker.in' or 'https://pub-<hash>.r2.dev'; found '{publicBaseUrl}'.");
        }

        options.R2.PublicBaseUrl = publicBaseUrl.TrimEnd('/');
        options.R2.AccountId = options.R2.AccountId.Trim();
        options.R2.BucketName = options.R2.BucketName.Trim();
        options.Provider = StorageOptions.ProviderR2;

        return options;
    }
}
