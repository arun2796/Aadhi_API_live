using System.Text.Json;
using AadhiCrackers.Api.Serialization;
using FluentAssertions;

namespace AadhiCrackers.Api.Tests;

/// <summary>
/// Every timestamp column is PostgreSQL "timestamp with time zone" and Npgsql refuses a
/// DateTime whose Kind is not Utc. These tests pin down that nothing with another Kind can get
/// past deserialization.
/// </summary>
public class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new NullableUtcDateTimeJsonConverter());
        return options;
    }

    private sealed class Payload
    {
        public DateTime StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
    }

    [Theory]
    // No offset — what a plain date picker sends. Previously deserialized as Unspecified.
    [InlineData("2026-12-01T00:00:00")]
    // Explicit UTC.
    [InlineData("2026-12-01T00:00:00Z")]
    // A real offset: the same instant, expressed elsewhere.
    [InlineData("2026-12-01T05:30:00+05:30")]
    // Date only.
    [InlineData("2026-12-01")]
    public void EveryAcceptedFormat_DeserializesAsUtc(string value)
    {
        var payload = JsonSerializer.Deserialize<Payload>(
            $$"""{"StartDateUtc":"{{value}}","EndDateUtc":"{{value}}"}""", Options)!;

        payload.StartDateUtc.Kind.Should().Be(DateTimeKind.Utc);
        payload.EndDateUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AnOffsetIsConverted_NotReinterpreted()
    {
        // 05:30 at +05:30 is midnight UTC — the instant must be preserved, not relabelled.
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"StartDateUtc":"2026-12-01T05:30:00+05:30"}""", Options)!;

        payload.StartDateUtc.Should().Be(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AValueWithoutAnOffset_IsReadAsUtc_NotShifted()
    {
        // No timezone information exists in the input, so the wall-clock value is kept as-is
        // and simply labelled UTC. Shifting it would invent a timezone the caller never sent.
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"StartDateUtc":"2026-12-01T09:15:00"}""", Options)!;

        payload.StartDateUtc.Should().Be(new DateTime(2026, 12, 1, 9, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NullStaysNull()
    {
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"StartDateUtc":"2026-12-01T00:00:00Z","EndDateUtc":null}""", Options)!;

        payload.EndDateUtc.Should().BeNull();
    }

    [Fact]
    public void Serialization_WritesUtc()
    {
        var json = JsonSerializer.Serialize(
            new Payload { StartDateUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) }, Options);

        json.Should().Contain("2026-12-01T00:00:00Z");
    }
}
