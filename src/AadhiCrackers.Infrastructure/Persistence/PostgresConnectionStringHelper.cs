using Npgsql;

namespace AadhiCrackers.Infrastructure.Persistence;

/// <summary>
/// Npgsql does not understand URI-style connection strings (postgres:// / postgresql://) such as
/// the ones handed out by Neon, Heroku, Render or Supabase. This helper converts a URI into the
/// keyword=value form Npgsql expects, so a hosted database URL can be pasted into
/// ConnectionStrings__DefaultConnection as-is. Strings already in keyword form are returned unchanged.
/// </summary>
public static class PostgresConnectionStringHelper
{
    /// <summary>
    /// Converts a postgres(ql):// URI into Npgsql keyword form (Host, Port, Database, Username,
    /// Password, SSL Mode, Channel Binding). Recognized query parameters: sslmode,
    /// channel_binding, application_name. Unknown query parameters are ignored on purpose.
    /// Non-URI input is returned as-is.
    /// </summary>
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString ?? string.Empty;
        }

        var trimmed = connectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);

        // NpgsqlConnectionStringBuilder takes care of quoting/escaping special characters.
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
        };

        if (uri.Port > 0)
        {
            builder.Port = uri.Port;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
            if (userInfo.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(userInfo[1]);
            }
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(keyValue[0]).Trim().ToLowerInvariant();
            var value = keyValue.Length == 2 ? Uri.UnescapeDataString(keyValue[1]).Trim() : string.Empty;

            switch (key)
            {
                case "sslmode":
                    builder.SslMode = ParseSslMode(value);
                    break;
                case "channel_binding":
                case "channelbinding":
                    builder.ChannelBinding = ParseChannelBinding(value);
                    break;
                case "application_name":
                    builder.ApplicationName = value;
                    break;
                default:
                    // Ignore parameters Npgsql has no keyword for (options, target_session_attrs, ...).
                    break;
            }
        }

        return builder.ConnectionString;
    }

    private static SslMode ParseSslMode(string value) => value.ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "require" => SslMode.Require,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => SslMode.Require
    };

    private static ChannelBinding ParseChannelBinding(string value) => value.ToLowerInvariant() switch
    {
        "disable" => ChannelBinding.Disable,
        "prefer" => ChannelBinding.Prefer,
        "require" => ChannelBinding.Require,
        _ => ChannelBinding.Prefer
    };
}
