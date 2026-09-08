namespace AadhiCrackers.Domain.Enums;

public enum ProductType
{
    Simple = 1,
    Variant = 2,
    Bundle = 3
}

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    Packed = 4,
    Shipped = 5,
    OutForDelivery = 6,
    Delivered = 7,
    Cancelled = 8,
    Returned = 9
}

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Paid = 3,
    Failed = 4,
    RefundPending = 5,
    Refunded = 6,
    Cancelled = 7,
    PartiallyPaid = 8
}

public enum PaymentMethod
{
    Cash = 1,
    UPI = 2,
    CreditCard = 3,
    DebitCard = 4,
    NetBanking = 5,
    Wallet = 6,
    BankTransfer = 7,
    COD = 8
}

public enum FulfillmentStatus
{
    Unfulfilled = 1,
    PartiallyPacked = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Returned = 6
}

public enum StockMovementType
{
    OpeningStock = 1,
    Purchase = 2,
    Sale = 3,
    Return = 4,
    Adjustment = 5,
    TransferIn = 6,
    TransferOut = 7,
    Damage = 8,
    StockReserved = 9,
    StockReservationReleased = 10
}

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5,
    Overdue = 6
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    PartiallyReceived = 4,
    Received = 5,
    Cancelled = 6,
    Rejected = 7
}

public enum ExpenseCategory
{
    Transport = 1,
    Packaging = 2,
    Electricity = 3,
    Rent = 4,
    Salary = 5,
    Marketing = 6,
    Warehouse = 7,
    Office = 8,
    Other = 9
}

public enum DiscountType
{
    None = 0,
    Percentage = 1,
    FixedAmount = 2
}

public enum AddressType
{
    Shipping = 1,
    Billing = 2,
    Both = 3
}

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Login = 4,
    Logout = 5,
    LoginFailed = 6,
    PasswordChanged = 7,
    RoleChanged = 8,
    PermissionChanged = 9,
    OrderCreated = 10,
    OrderStatusChanged = 11,
    OrderCancelled = 12,
    PaymentCreated = 13,
    InvoiceGenerated = 14,
    StockAdjusted = 15,
    StockTransferred = 16,
    PurchaseCreated = 17,
    GoodsReceived = 18,
    CustomerUpdated = 19,
    ExportGenerated = 20,
    SettingsChanged = 21,
    PaymentRejected = 22,
    PaymentVerified = 23,
    RefundProcessed = 24,
    ReturnRequested = 25,
    ReturnInspected = 26,
    PurchaseApproved = 27,
    PurchaseRejected = 28,
    ReturnApproved = 29,
    ReturnReceived = 30,
    QuoteCreated = 31,
    QuoteConverted = 32,
    QuoteStatusChanged = 33,
    ReturnRejected = 34
}

public enum AuditSeverity
{
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public enum PromotionStatus
{
    Draft = 1,
    Scheduled = 2,
    Active = 3,
    Expired = 4,
    Disabled = 5
}

public enum EnquirySource
{
    Direct = 1,
    Website = 2,
    Phone = 3,
    WhatsApp = 4
}

public enum EnquiryStatus
{
    New = 1,
    Contacted = 2,
    Quoted = 3,
    Converted = 4,
    Closed = 5
}

