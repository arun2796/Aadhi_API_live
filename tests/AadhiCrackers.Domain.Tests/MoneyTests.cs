using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AadhiCrackers.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void FromDecimal_ShouldStoreCorrectMinorUnits()
    {
        var money = Money.FromDecimal(2499.50m);
        money.AmountMinor.Should().Be(249950);
        money.ToDecimal().Should().Be(2499.50m);
        money.Currency.Should().Be("INR");
    }

    [Fact]
    public void Addition_ShouldCalculateCorrectSum()
    {
        var a = Money.FromDecimal(100.25m);
        var b = Money.FromDecimal(50.75m);
        var sum = a + b;

        sum.ToDecimal().Should().Be(151.00m);
        sum.AmountMinor.Should().Be(15100);
    }

    [Fact]
    public void Subtraction_ShouldCalculateCorrectDifference()
    {
        var a = Money.FromDecimal(500m);
        var b = Money.FromDecimal(150.50m);
        var diff = a - b;

        diff.ToDecimal().Should().Be(349.50m);
    }

    [Fact]
    public void Multiplication_ShouldCalculateCorrectProduct()
    {
        var money = Money.FromDecimal(100m);
        var multiplied = money * 0.18m; // 18% GST

        multiplied.ToDecimal().Should().Be(18.00m);
        multiplied.AmountMinor.Should().Be(1800);
    }

    [Fact]
    public void Comparisons_ShouldWorkAsExpected()
    {
        var low = Money.FromDecimal(100m);
        var high = Money.FromDecimal(200m);

        (low < high).Should().BeTrue();
        (high > low).Should().BeTrue();
        (low <= high).Should().BeTrue();
        (high >= low).Should().BeTrue();
        (low == Money.FromDecimal(100m)).Should().BeTrue();
    }
}
