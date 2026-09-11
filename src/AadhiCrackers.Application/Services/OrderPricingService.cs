using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

/// <summary>
/// One priced line going into a quote or an order. Carrying the tax rate (rather than a
/// pre-computed tax amount) is what lets the cart quote and the order run the identical arithmetic.
/// </summary>
public readonly record struct PricedLine(Money UnitPrice, int Quantity, decimal TaxRatePercent)
{
    public Money LineTotalBeforeTax => UnitPrice * Quantity;
    public Money Tax => LineTotalBeforeTax * (TaxRatePercent / 100m);
}

public sealed record OrderTotals
{
    public Money ItemsSubtotal { get; init; } = Money.Zero();
    public Money Discount { get; init; } = Money.Zero();
    public Money Tax { get; init; } = Money.Zero();

    public Money ShippingCharge { get; init; } = Money.Zero();

    public Money PackingCharges { get; init; } = Money.Zero();
    public decimal PackingChargePercent { get; init; }
    public Money GrandTotal { get; init; } = Money.Zero();
}

public interface IOrderPricingService
{
    /// <summary>Packing charge rate (a percentage, e.g. 1.5 for 1.5%) from system settings.</summary>
    Task<decimal> GetPackingChargePercentAsync(CancellationToken cancellationToken = default);

    Task<(Promotion? Promotion, Money Discount)> ResolveCouponAsync(
        string? couponCode, Money itemsSubtotal, Guid? customerId, CancellationToken cancellationToken = default);

    /// <summary>Computes the full breakdown for a set of priced lines.</summary>
    OrderTotals CalculateTotals(IEnumerable<PricedLine> lines, Money discount, decimal packingChargePercent);

    /// <summary>
    /// The customer an order placed by this caller would be attributed to — the identity the
    /// per-customer coupon limit is counted against. A cart quote MUST resolve it the same way
    /// the order will, or the two disagree the moment a coupon has a PerCustomerLimit.
    /// </summary>
    Task<Guid?> ResolveOrderCustomerIdAsync(string? currentUserEmail, CancellationToken cancellationToken = default);
}

public class OrderPricingService : IOrderPricingService
{
    public const string PackingChargePercentSettingKey = "Order.PackingChargePercent";
    public const decimal DefaultPackingChargePercent = 1.5m;

    /// <summary>Anonymous checkouts are all attributed to this one customer record.</summary>
    public const string GuestCustomerEmail = "guest@aadhicracker.in";

    /// <summary>The email an order placed by this caller is attributed to.</summary>
    public static string ResolveOrderCustomerEmail(string? currentUserEmail) =>
        string.IsNullOrWhiteSpace(currentUserEmail) ? GuestCustomerEmail : currentUserEmail;

    /// <summary>
    /// True when this email is the SHARED guest bucket rather than one real person. Every anonymous
    /// checkout lands on that single customer record, so anything that answers "show me everything
    /// belonging to this customer" must refuse it - one guest would otherwise be handed every other
    /// guest's data. Used by the notification feature to decide ownership.
    /// </summary>
    public static bool IsSharedGuestBucket(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        string.Equals(email.Trim(), GuestCustomerEmail, StringComparison.OrdinalIgnoreCase);

    private readonly IApplicationDbContext _context;

    public OrderPricingService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetPackingChargePercentAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _context.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == PackingChargePercentSettingKey && !s.IsDeleted)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrWhiteSpace(raw)
            && decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var percent)
            && percent >= 0m
                ? percent
                : DefaultPackingChargePercent;
    }

    public async Task<(Promotion? Promotion, Money Discount)> ResolveCouponAsync(
        string? couponCode, Money itemsSubtotal, Guid? customerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(couponCode)) return (null, Money.Zero());

        var code = couponCode.Trim().ToUpper();
        var promo = await _context.Promotions
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == code && !p.IsDeleted, cancellationToken);

        if (promo == null) return (null, Money.Zero());

        int? customerUsageCount = null;
        if (customerId.HasValue)
        {
            customerUsageCount = await _context.PromotionRedemptions
                .CountAsync(r => r.PromotionId == promo.Id && r.CustomerId == customerId.Value && !r.IsDeleted, cancellationToken);
        }

        if (!promo.IsValidForOrder(itemsSubtotal, customerUsageCount)) return (null, Money.Zero());

        return (promo, promo.CalculateDiscount(itemsSubtotal));
    }

    public OrderTotals CalculateTotals(IEnumerable<PricedLine> lines, Money discount, decimal packingChargePercent) =>
        Calculate(lines, discount, packingChargePercent);

    public async Task<Guid?> ResolveOrderCustomerIdAsync(string? currentUserEmail, CancellationToken cancellationToken = default)
    {
        var email = ResolveOrderCustomerEmail(currentUserEmail);
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email.ToLower() == email.ToLower() && !c.IsDeleted, cancellationToken);

        return customer?.Id;
    }

    /// <summary>
    /// THE order arithmetic. Deliberately pure and static: the quote and the order both call it,
    /// so a change here cannot land on one side only.
    /// </summary>
    public static OrderTotals Calculate(IEnumerable<PricedLine> lines, Money discount, decimal packingChargePercent)
    {
        var itemsSubtotal = Money.Zero();
        var tax = Money.Zero();

        foreach (var line in lines)
        {
            itemsSubtotal += line.LineTotalBeforeTax;
            tax += line.Tax;
        }

        // Delivery is NEVER charged: goods travel by lorry and the customer pays the transport
        // company directly when collecting the parcel.
        var shippingCharge = Money.Zero();
        var packingCharges = CalculatePackingCharges(itemsSubtotal, packingChargePercent);

        return new OrderTotals
        {
            ItemsSubtotal = itemsSubtotal,
            Discount = discount,
            Tax = tax,
            ShippingCharge = shippingCharge,
            PackingCharges = packingCharges,
            PackingChargePercent = packingChargePercent,
            GrandTotal = itemsSubtotal - discount + tax + shippingCharge + packingCharges
        };
    }

    /// <summary>Packing charges: a real billed line, a percentage of the items subtotal.</summary>
    public static Money CalculatePackingCharges(Money itemsSubtotal, decimal percent) =>
        Money.FromDecimal(Math.Round(itemsSubtotal.ToDecimal() * percent / 100m, 2, MidpointRounding.AwayFromZero));
}
