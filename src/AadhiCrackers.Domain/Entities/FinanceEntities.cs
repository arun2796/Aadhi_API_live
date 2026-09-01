using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Invoice : AggregateRoot<Guid>
{
    public string InvoiceNumber { get; set; } = string.Empty; // e.g. INV-2026-000001
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Money Subtotal { get; set; } = Money.Zero();
    public Money Discount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money Shipping { get; set; } = Money.Zero();
    public Money GrandTotal { get; set; } = Money.Zero();
    public Money PaidAmount { get; set; } = Money.Zero();
    public Money BalanceAmount { get; set; } = Money.Zero();

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.AddDays(7);
}

public class Payment : AggregateRoot<Guid>
{
    public string PaymentNumber { get; set; } = string.Empty; // e.g. PAY-2026-000001
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Money Amount { get; set; } = Money.Zero();
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public string? TransactionReference { get; set; }
    public string? UtrNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Notes { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
}

public class Expense : AggregateRoot<Guid>
{
    public string ExpenseNumber { get; set; } = string.Empty; // e.g. EXP-2026-000001
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public string Description { get; set; } = string.Empty;
    public Money Amount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public DateTime ExpenseDateUtc { get; set; } = DateTime.UtcNow;
    public string? Reference { get; set; }
}

public class Refund : AggregateRoot<Guid>
{
    public string RefundNumber { get; set; } = string.Empty; // e.g. REF-2026-000001
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid? PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public Money Amount { get; set; } = Money.Zero();
    public string Reason { get; set; } = string.Empty;
    public PaymentMethod Method { get; set; } = PaymentMethod.UPI;
    public string Status { get; set; } = "Completed"; // Pending, Processing, Completed, Failed
    public string? Reference { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SupplierBill : AggregateRoot<Guid>
{
    public string BillNumber { get; set; } = string.Empty; // e.g. BIL-2026-000001
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public Money Subtotal { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money Discount { get; set; } = Money.Zero();
    public Money Total { get; set; } = Money.Zero();
    public Money PaidAmount { get; set; } = Money.Zero();
    public Money BalanceAmount { get; set; } = Money.Zero();
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.AddDays(30);
    public string Status { get; set; } = "Issued"; // Draft, Issued, PartiallyPaid, Paid, Overdue, Cancelled
}

public class ReturnOrder : AggregateRoot<Guid>
{
    public string ReturnNumber { get; set; } = string.Empty; // e.g. RET-2026-000001
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Requested"; // Requested, Approved, Rejected, Received, Inspected, Refunded, Closed
    public string? InspectionNotes { get; set; }
    public bool IsSellable { get; set; }
    public Money RefundAmount { get; set; } = Money.Zero();
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InspectedAtUtc { get; set; }

    public ICollection<ReturnOrderItem> Items { get; set; } = new List<ReturnOrderItem>();
}

public class ReturnOrderItem : BaseEntity<Guid>
{
    public Guid ReturnOrderId { get; set; }
    public ReturnOrder ReturnOrder { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public Money UnitPrice { get; set; } = Money.Zero();
    public bool IsDamaged { get; set; }
    public string? ConditionNotes { get; set; }
}
