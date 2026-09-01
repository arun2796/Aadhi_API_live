using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Quote : AggregateRoot<Guid>
{
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Money Subtotal { get; set; } = Money.Zero();
    public Money Discount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money GrandTotal { get; set; } = Money.Zero();
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime ExpiryDateUtc { get; set; }
    public string? Notes { get; set; }
    public Guid? ConvertedOrderId { get; set; }

    public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();

    public void CalculateTotals()
    {
        decimal sub = 0;
        foreach (var item in Items)
        {
            item.CalculateLineTotal();
            sub += item.LineTotal.ToDecimal();
        }

        Subtotal = Money.FromDecimal(sub);
        GrandTotal = Money.FromDecimal(Math.Max(0, sub - Discount.ToDecimal() + Tax.ToDecimal()));
    }
}

public class QuoteItem : BaseEntity<Guid>
{
    public Guid QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public Money UnitPrice { get; set; } = Money.Zero();
    public decimal DiscountPercentage { get; set; }
    public Money LineTotal { get; set; } = Money.Zero();

    public void CalculateLineTotal()
    {
        var rawTotal = Quantity * UnitPrice.ToDecimal();
        var discount = rawTotal * (DiscountPercentage / 100m);
        LineTotal = Money.FromDecimal(Math.Max(0, rawTotal - discount));
    }
}
