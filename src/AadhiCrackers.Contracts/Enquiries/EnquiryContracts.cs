using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Enquiries;

public class EnquiryItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal? ExpectedPrice { get; set; }
    public decimal? QuotedPrice { get; set; }
    public string? Note { get; set; }
}

public class EnquiryDto
{
    public Guid Id { get; set; }
    public string EnquiryNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string Source { get; set; } = "Direct";
    public string Status { get; set; } = "New";
    public string? Notes { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public List<EnquiryItemDto> Items { get; set; } = new();
}

public class CreateEnquiryItemRequest
{
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal? ExpectedPrice { get; set; }
    public decimal? QuotedPrice { get; set; }
    public string? Note { get; set; }
}

public class CreateEnquiryRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public EnquirySource Source { get; set; } = EnquirySource.Direct;
    public string? Notes { get; set; }
    public Guid? CustomerId { get; set; }
    public List<CreateEnquiryItemRequest> Items { get; set; } = new();
}

public class UpdateEnquiryRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public EnquirySource Source { get; set; } = EnquirySource.Direct;
    public string? Notes { get; set; }
    public Guid? CustomerId { get; set; }
    public List<CreateEnquiryItemRequest> Items { get; set; } = new();
}

public class UpdateEnquiryStatusRequest
{
    public EnquiryStatus Status { get; set; }
    public string? Note { get; set; }
}

public class EnquiryCustomerDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int EnquiryCount { get; set; }
    public DateTime LastEnquiryDateUtc { get; set; }
}
