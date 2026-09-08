namespace AadhiCrackers.Contracts.Inventory;

public class LowStockAlertDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int CurrentStock { get; set; }
    public int ReorderLevel { get; set; }
    public string Status { get; set; } = "Low";
}
