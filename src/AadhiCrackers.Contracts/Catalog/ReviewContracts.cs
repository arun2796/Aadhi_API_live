namespace AadhiCrackers.Contracts.Catalog;

public class ProductReviewDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Approved"; // Pending, Approved, Rejected, Hidden
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateProductReviewRequest
{
    public Guid ProductId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
}

public class UpdateReviewStatusRequest
{
    public string Status { get; set; } = "Approved"; // Pending, Approved, Rejected, Hidden
    public string? ModerationNotes { get; set; }
}
