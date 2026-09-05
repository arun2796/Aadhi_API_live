using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Contracts.Orders;

public class CartItemDto
{
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int Quantity { get; set; }
    public int MaxStock { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class CartDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public int TotalItems => Items.Sum(i => i.Quantity);
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal Discount { get; set; }
    public string? CouponCode { get; set; }
    public decimal ShippingCharge { get; set; } // computed server-side from SystemSettings (Delivery.* keys)
    public decimal GrandTotal => Math.Max(0, Subtotal - Discount + ShippingCharge);
}

public class AddToCartRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class UpdateCartItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public class ApplyCouponRequest
{
    public string Code { get; set; } = string.Empty;
}

public class CustomerAddressDto
{
    public Guid Id { get; set; }
    public AddressType AddressType { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "India";
    public bool IsDefault { get; set; }
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderStatusHistoryDto
{
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

public class OrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public FulfillmentStatus FulfillmentStatus { get; set; }
    public decimal ItemsSubtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal ShippingCharge { get; set; }
    public decimal GrandTotal { get; set; }
    public string? CouponCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime PlacedAtUtc { get; set; }

    // Delivery
    public string DeliveryMethod { get; set; } = "standard";
    public DateTime? ExpectedDeliveryFrom { get; set; }
    public DateTime? ExpectedDeliveryTo { get; set; }

    // Reward points earnable once delivered: floor(total / 100)
    public int RewardPointsEarnable => (int)Math.Floor(GrandTotal / 100m);

    // UPI QR Code Verification Details
    public string? UtrNumber { get; set; }
    public string? PaymentScreenshotUrl { get; set; }
    public DateTime? PaymentSubmittedAtUtc { get; set; }
    public DateTime? PaymentVerifiedAtUtc { get; set; }
    public string? PaymentVerifiedBy { get; set; }
    public string? PaymentVerificationNotes { get; set; }

    public CustomerAddressDto ShippingAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistories { get; set; } = new();
}

public class CreateOrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public Guid? WarehouseId { get; set; }
    public Address ShippingAddress { get; set; } = new();
    public Address? BillingAddress { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.UPI;
    public string? DeliveryMethod { get; set; } // "standard" (default) | "express"
    public string? CouponCode { get; set; }
    public string? Notes { get; set; }
    
    // UPI QR Code & Screenshot Proof
    public string? UtrNumber { get; set; }
    public string? PaymentScreenshotBase64 { get; set; }
    public string? PaymentScreenshotUrl { get; set; }

    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class SubmitPaymentProofRequest
{
    public string UtrNumber { get; set; } = string.Empty;
    public string? ScreenshotBase64 { get; set; }
    public string? PaymentScreenshotBase64 { get; set; }
    public string? PaymentScreenshotUrl { get; set; }
    public string? Notes { get; set; }

    // Anonymous submissions must supply the matching order number (IDOR guard)
    public string? OrderNumber { get; set; }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public class VerifyPaymentRequest
{
    public string? VerifiedUtrNumber { get; set; }
    public string? VerificationNotes { get; set; }
    public bool AutoMoveToPacking { get; set; } = false;
}

public class RejectPaymentRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class OrderTrackingDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? UtrNumber { get; set; }
    public string? PaymentScreenshotUrl { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public DateTime? EstimatedDeliveryUtc { get; set; }
    public string DeliveryMethod { get; set; } = "standard";
    public DateTime? ExpectedDeliveryFrom { get; set; }
    public DateTime? ExpectedDeliveryTo { get; set; }
    public string? TrackingNumber { get; set; }
    public string DeliveryAddressSummary { get; set; } = string.Empty;
    public List<OrderStatusHistoryDto> Timeline { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public decimal GrandTotal { get; set; }
}

public class ReturnOrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
    public bool IsDamaged { get; set; }
    public string? ConditionNotes { get; set; }
}

public class ReturnOrderDto
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Requested";
    public string? InspectionNotes { get; set; }
    public bool IsSellable { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? InspectedAtUtc { get; set; }
    public List<ReturnOrderItemDto> Items { get; set; } = new();
}

public class CreateReturnOrderItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? OrderItemId { get; set; } // alternative to ProductId — resolved to the order line
    public int Quantity { get; set; }
    public string? Reason { get; set; }
}

public class CreateReturnOrderRequest
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public List<CreateReturnOrderItemRequest> Items { get; set; } = new();
}

public class InspectReturnItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public bool IsSellable { get; set; }
    public string? ConditionNotes { get; set; }
}

public class InspectReturnOrderRequest
{
    public string? InspectionNotes { get; set; }
    public List<InspectReturnItemRequest> ItemInspections { get; set; } = new();
}

public class RejectReturnOrderRequest
{
    public string? Reason { get; set; }
}

public class DeliveryOptionDto
{
    public string Code { get; set; } = string.Empty; // standard | express
    public string Name { get; set; } = string.Empty;
    public decimal Charge { get; set; }
    public int EtaMinDays { get; set; }
    public int EtaMaxDays { get; set; }
}

// ---- Customer-facing return visibility (GET /returns/my) ----

public class CustomerReturnItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CustomerReturnRefundDto
{
    public string RefundNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
}

public class CustomerReturnTimelineEntryDto
{
    public string Status { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public bool Completed { get; set; }
}

public class CustomerReturnDto
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<CustomerReturnItemDto> Items { get; set; } = new();
    public CustomerReturnRefundDto? Refund { get; set; }
    public List<CustomerReturnTimelineEntryDto> Timeline { get; set; } = new();
}
