using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class OrderStatusHistory : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OrderItem : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    // Immutable Snapshots
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SKUSnapshot { get; set; } = string.Empty;
    public string? ProductImageUrlSnapshot { get; set; }
    public Money UnitPrice { get; set; } = Money.Zero();
    public Money CostPriceSnapshot { get; set; } = Money.Zero();
    public int Quantity { get; set; }
    public Money Discount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money LineTotal { get; set; } = Money.Zero();
}

public class Order : AggregateRoot<Guid>
{
    public string OrderNumber { get; set; } = string.Empty; // e.g. ORD-2026-000001
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.COD;
    public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Unfulfilled;

    public Money ItemsSubtotal { get; set; } = Money.Zero();
    public Money Discount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money ShippingCharge { get; set; } = Money.Zero();
    public Money GrandTotal { get; set; } = Money.Zero();

    public string? CouponCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime PlacedAtUtc { get; set; } = DateTime.UtcNow;
    public string DeliveryMethod { get; set; } = "standard"; // standard | express
    public bool RewardPointsAwarded { get; set; }

    // UPI QR Code Payment Proof & Verification
    public string? UtrNumber { get; set; }
    public string? PaymentScreenshotUrl { get; set; }
    public DateTime? PaymentSubmittedAtUtc { get; set; }
    public DateTime? PaymentVerifiedAtUtc { get; set; }
    public string? PaymentVerifiedBy { get; set; }
    public string? PaymentVerificationNotes { get; set; }

    public Address ShippingAddress { get; set; } = new();
    public Address? BillingAddress { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public bool CanTransitionTo(OrderStatus nextStatus)
    {
        if (OrderStatus == nextStatus) return false;

        return OrderStatus switch
        {
            OrderStatus.Pending => nextStatus is OrderStatus.Confirmed or OrderStatus.Cancelled,
            OrderStatus.Confirmed => nextStatus is OrderStatus.Processing or OrderStatus.Packed or OrderStatus.Shipped or OrderStatus.Cancelled,
            OrderStatus.Processing => nextStatus is OrderStatus.Packed or OrderStatus.Shipped or OrderStatus.Cancelled,
            OrderStatus.Packed => nextStatus is OrderStatus.Shipped or OrderStatus.Cancelled,
            OrderStatus.Shipped => nextStatus is OrderStatus.OutForDelivery or OrderStatus.Delivered,
            OrderStatus.OutForDelivery => nextStatus is OrderStatus.Delivered,
            OrderStatus.Delivered => false,
            OrderStatus.Cancelled => false,
            OrderStatus.Returned => false,
            _ => false
        };
    }

    public void ChangeStatus(OrderStatus newStatus, string? reason = null, string? changedBy = null)
    {
        if (!CanTransitionTo(newStatus))
        {
            throw new InvalidOrderStateTransitionException(OrderStatus.ToString(), newStatus.ToString());
        }

        var oldStatus = OrderStatus;
        OrderStatus = newStatus;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedBy = changedBy;

        StatusHistories.Add(new OrderStatusHistory
        {
            OrderId = Id,
            FromStatus = oldStatus,
            ToStatus = newStatus,
            Reason = reason,
            ChangedBy = changedBy,
            ChangedAtUtc = DateTime.UtcNow
        });

        // Sync fulfillment status where applicable
        if (newStatus == OrderStatus.Packed) FulfillmentStatus = FulfillmentStatus.Packed;
        else if (newStatus == OrderStatus.Shipped) FulfillmentStatus = FulfillmentStatus.Shipped;
        else if (newStatus == OrderStatus.Delivered) FulfillmentStatus = FulfillmentStatus.Delivered;
    }
}
