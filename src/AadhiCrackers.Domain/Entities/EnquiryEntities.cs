using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Domain.Entities;

public class Enquiry : AggregateRoot<Guid>
{
    public string EnquiryNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public EnquirySource Source { get; set; } = EnquirySource.Direct;
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
    public string? Notes { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public ICollection<EnquiryItem> Items { get; set; } = new List<EnquiryItem>();
}

public class EnquiryItem : BaseEntity<Guid>
{
    public Guid EnquiryId { get; set; }
    public Enquiry? Enquiry { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal? ExpectedPrice { get; set; }
    public decimal? QuotedPrice { get; set; }
    public string? Note { get; set; }
}
