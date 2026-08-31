using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AadhiCrackers.Infrastructure.Persistence.ValueConverters;

public class MoneyConverter : ValueConverter<Money, long>
{
    public MoneyConverter() : base(
        money => money.AmountMinor,
        minor => Money.FromMinor(minor, Money.DefaultCurrency))
    {
    }
}
