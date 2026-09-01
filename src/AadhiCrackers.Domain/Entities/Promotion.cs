using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Promotion : BaseEntity<Guid>
{
    public string Code { get; set; } = string.Empty; // e.g. DIWALI2026, WELCOME10
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public Money? MinimumOrderAmount { get; set; }
    public Money? MaximumDiscount { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public int? PerCustomerLimit { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public PromotionStatus Status { get; set; } = PromotionStatus.Active;
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public ICollection<PromotionRedemption> Redemptions { get; set; } = new List<PromotionRedemption>();

    public bool IsValidForOrder(Money subtotal, int? customerUsageCount = null, DateTime? checkTimeUtc = null)
    {
        var now = checkTimeUtc ?? DateTime.UtcNow;

        if (!IsActive || Status != PromotionStatus.Active) return false;
        if (StartDateUtc.HasValue && now < StartDateUtc.Value) return false;
        if (EndDateUtc.HasValue && now > EndDateUtc.Value) return false;
        if (UsageLimit.HasValue && UsedCount >= UsageLimit.Value) return false;
        if (MinimumOrderAmount.HasValue && subtotal < MinimumOrderAmount.Value) return false;
        if (PerCustomerLimit.HasValue && customerUsageCount.HasValue && customerUsageCount.Value >= PerCustomerLimit.Value) return false;

        return true;
    }

    public Money CalculateDiscount(Money subtotal)
    {
        if (!IsValidForOrder(subtotal)) return Money.Zero();

        Money calculatedDiscount;
        if (DiscountType == DiscountType.Percentage)
        {
            var fraction = DiscountValue / 100m;
            calculatedDiscount = subtotal * fraction;
        }
        else if (DiscountType == DiscountType.FixedAmount)
        {
            calculatedDiscount = Money.FromDecimal(DiscountValue);
        }
        else
        {
            return Money.Zero();
        }

        if (MaximumDiscount.HasValue && calculatedDiscount > MaximumDiscount.Value)
        {
            calculatedDiscount = MaximumDiscount.Value;
        }

        if (calculatedDiscount > subtotal)
        {
            calculatedDiscount = subtotal;
        }

        return calculatedDiscount;
    }
}

public class PromotionRedemption : BaseEntity<Guid>
{
    public Guid PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
