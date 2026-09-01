using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Supplier : BaseEntity<Guid>
{
    public string Code { get; set; } = string.Empty; // e.g. SUP-001
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? GstNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

public class PurchaseOrderItem : BaseEntity<Guid>
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SKUSnapshot { get; set; } = string.Empty;
    public Money UnitPrice { get; set; } = Money.Zero();
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public Money LineTotal { get; set; } = Money.Zero();
}

public class PurchaseOrder : AggregateRoot<Guid>
{
    public string PoNumber { get; set; } = string.Empty; // e.g. PO-2026-000001
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public Money Subtotal { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money GrandTotal { get; set; } = Money.Zero();
    public DateTime OrderDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDateUtc { get; set; }
    public string? Notes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}

public class GoodsReceiptItem : BaseEntity<Guid>
{
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
    public Guid PurchaseOrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityAccepted { get; set; }
    public int QuantityRejected { get; set; }
    public int QuantityDamaged { get; set; }
    public string? RejectionReason { get; set; }

    public Money UnitPrice { get; set; } = Money.Zero();
    public Money LineTotal { get; set; } = Money.Zero();
}

public class GoodsReceipt : AggregateRoot<Guid>
{
    public string ReceiptNumber { get; set; } = string.Empty; // e.g. GRN-2026-000001
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateTime ReceivedDateUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
}
