using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Inventory;

public class WarehouseDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public bool IsPrimary { get; set; }
    public int TotalProducts { get; set; }
    public int TotalStock { get; set; }
}

public class StockItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int QuantityAvailable { get; set; }
    public int ReorderLevel { get; set; }
    public string Status => QuantityAvailable == 0 ? "Out of Stock" : (QuantityAvailable <= ReorderLevel ? "Low Stock" : "In Stock");
}

public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public StockMovementType MovementType { get; set; }
    public int QuantityChange { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class StockAdjustmentRequest
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public int AdjustedQuantity { get; set; } // absolute new on-hand OR relative change
    public bool IsRelative { get; set; } // true if adding/subtracting, false if setting new total
    public string Reason { get; set; } = string.Empty;
}

public class StockTransferRequest
{
    public Guid ProductId { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

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

public class SupplierDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? GstNumber { get; set; }
    public bool IsActive { get; set; }
    public int TotalPurchaseOrders { get; set; }
}

public class CreateSupplierRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? GstNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PurchaseOrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal GrandTotal { get; set; }
    public DateTime OrderDateUtc { get; set; }
    public DateTime? ExpectedDeliveryDateUtc { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}

public class CreatePurchaseOrderItemRequest
{
    public Guid ProductId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class CreatePurchaseOrderRequest
{
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime? ExpectedDeliveryDateUtc { get; set; }
    public string? Notes { get; set; }
    public List<CreatePurchaseOrderItemRequest> Items { get; set; } = new();
}

public class GoodsReceiptItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantityReceived { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class GoodsReceiptDto
{
    public Guid Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid PurchaseOrderId { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime ReceivedDateUtc { get; set; }
    public string? Notes { get; set; }
    public List<GoodsReceiptItemDto> Items { get; set; } = new();
}

public class CreateGoodsReceiptItemRequest
{
    public Guid PurchaseOrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CreateGoodsReceiptRequest
{
    public Guid PurchaseOrderId { get; set; }
    public string? Notes { get; set; }
    public List<CreateGoodsReceiptItemRequest> Items { get; set; } = new();
}
