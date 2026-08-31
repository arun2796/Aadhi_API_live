using System.Globalization;

namespace AadhiCrackers.Domain.ValueObjects;

public readonly record struct Money : IComparable<Money>, IComparable
{
    public long AmountMinor { get; }
    public string Currency { get; }

    public const string DefaultCurrency = "INR";

    public Money(long amountMinor, string currency = DefaultCurrency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty.", nameof(currency));

        AmountMinor = amountMinor;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency = DefaultCurrency) => new(0, currency);

    public static Money FromMinor(long amountMinor, string currency = DefaultCurrency) =>
        new(amountMinor, currency);

    public static Money FromDecimal(decimal amount, string currency = DefaultCurrency)
    {
        var minor = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        return new Money(minor, currency);
    }

    public decimal ToDecimal() => AmountMinor / 100m;

    public string Format(bool includeSymbol = true)
    {
        var dec = ToDecimal();
        var culture = new CultureInfo("en-IN");
        var formatted = dec.ToString("N2", culture);
        if (!includeSymbol) return formatted;
        return Currency == "INR" ? $"₹{formatted}" : $"{Currency} {formatted}";
    }

    public override string ToString() => Format();

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountMinor + b.AmountMinor, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountMinor - b.AmountMinor, a.Currency);
    }

    public static Money operator *(Money a, decimal multiplier)
    {
        var minor = (long)Math.Round(a.AmountMinor * multiplier, MidpointRounding.AwayFromZero);
        return new Money(minor, a.Currency);
    }

    public static Money operator *(decimal multiplier, Money a) => a * multiplier;

    public static Money operator *(Money a, int multiplier) =>
        new(a.AmountMinor * multiplier, a.Currency);

    public static Money operator *(int multiplier, Money a) => a * multiplier;

    public static bool operator <(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor < b.AmountMinor;
    }

    public static bool operator >(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor > b.AmountMinor;
    }

    public static bool operator <=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor <= b.AmountMinor;
    }

    public static bool operator >=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.AmountMinor >= b.AmountMinor;
    }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Cannot operate on money with different currencies: '{a.Currency}' and '{b.Currency}'.");
    }

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return AmountMinor.CompareTo(other.AmountMinor);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is Money other) return CompareTo(other);
        throw new ArgumentException("Object must be of type Money", nameof(obj));
    }
}
