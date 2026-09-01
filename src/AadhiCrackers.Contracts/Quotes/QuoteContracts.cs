using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Quotes;

public class QuoteItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal LineTotal { get; set; }
}

public class QuoteDto
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime ExpiryDateUtc { get; set; }
    public string? Notes { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<QuoteItemDto> Items { get; set; } = new();
}

public class CreateQuoteItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal? CustomUnitPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
}

public class CreateQuoteRequest
{
    public Guid CustomerId { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public string? Notes { get; set; }
    public decimal DiscountAmount { get; set; }
    public List<CreateQuoteItemRequest> Items { get; set; } = new();
}

public class UpdateQuoteStatusRequest
{
    public QuoteStatus Status { get; set; }
    public string? Notes { get; set; }
}
