namespace AadhiCrackers.Domain.ValueObjects;

public sealed record Address
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = "India";

    public Address() { }

    public Address(
        string fullName,
        string phone,
        string addressLine1,
        string? addressLine2,
        string city,
        string state,
        string postalCode,
        string country = "India")
    {
        FullName = fullName.Trim();
        Phone = phone.Trim();
        AddressLine1 = addressLine1.Trim();
        AddressLine2 = addressLine2?.Trim();
        City = city.Trim();
        State = state.Trim();
        PostalCode = postalCode.Trim();
        Country = country.Trim();
    }

    public string ToSingleLine()
    {
        var parts = new[] { FullName, AddressLine1, AddressLine2, City, State, PostalCode, Country }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }
}
