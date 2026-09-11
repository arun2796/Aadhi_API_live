using System.Text.Json;
using System.Text.Json.Serialization;

namespace AadhiCrackers.Api.Serialization;

/// <summary>
/// Forces every <see cref="DateTime"/> crossing the JSON boundary to be UTC.
///
/// WHY THIS EXISTS. Every timestamp column in this database is PostgreSQL
/// <c>timestamp with time zone</c> (121 of them), and Npgsql refuses to write a
/// <see cref="DateTime"/> whose <see cref="DateTime.Kind"/> is not
/// <see cref="DateTimeKind.Utc"/> — it throws
/// "Cannot write DateTime with Kind=Unspecified ... only UTC is supported" at SaveChanges.
///
/// System.Text.Json produces exactly that Kind for any JSON value without an offset, which is
/// what a plain date picker sends ("2026-12-01T00:00:00"). So a caller that omits the trailing
/// "Z" — a new screen, a Swagger try-it-out, a mobile client — would fail deep inside the
/// persistence layer with an error that says nothing about the request. Callers that DO send an
/// offset are unaffected: their value is converted to UTC, which is the same instant.
///
/// This cannot silently corrupt data. A value without an offset carries no timezone information
/// at all, so treating it as UTC is the only interpretation available; the alternative is a
/// 500 response.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToUtc(value));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        // Carries a real offset — convert to the same instant in UTC.
        DateTimeKind.Local => value.ToUniversalTime(),
        // No timezone information was supplied; read it as UTC rather than guessing a zone.
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

/// <summary>Nullable counterpart; <c>JsonConverter&lt;DateTime&gt;</c> does not cover <c>DateTime?</c>.</summary>
public sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null
            ? null
            : UtcDateTimeJsonConverter.ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTimeJsonConverter.ToUtc(value.Value));
    }
}
