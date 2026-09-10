using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Notifications;

/// <summary>
/// One stored customer-facing notification. `type` serialises as the enum NAME
/// ("OrderPlaced" | "PaymentVerified" | "PaymentRejected" | "OrderStatusChanged" | "OrderDispatched")
/// because the API registers JsonStringEnumConverter globally.
///
/// The carrier block is populated on OrderDispatched notifications and is the reason this feature
/// exists: it tells the customer which transport company holds the parcel, the LR / waybill number
/// to quote, and the office phone and address to collect it from.
/// </summary>
public class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    /// <summary>Order status when the notification was raised, e.g. "Shipped". Null when not order-scoped.</summary>
    public string? OrderStatus { get; set; }

    // Transport / dispatch details - non-null on OrderDispatched.
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CarrierPhone { get; set; }
    public string? CarrierAddress { get; set; }

    /// <summary>
    /// Optional flat bag of extra display values (e.g. "reason", "grandTotal", "previousStatus").
    /// Always a JSON object of string values, or null.
    /// </summary>
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>
/// Paged list of the signed-in customer's notifications plus the unread badge count.
/// Inherits the standard PagedResult envelope (items / pageNumber / pageSize / totalCount /
/// totalPages / hasPreviousPage / hasNextPage).
/// </summary>
public class NotificationListResponse : PagedResult<NotificationDto>
{
    /// <summary>Unread notifications for this customer across ALL pages, for the bell badge.</summary>
    public int UnreadCount { get; set; }

    public NotificationListResponse() { }

    public NotificationListResponse(IReadOnlyList<NotificationDto> items, int count, int pageNumber, int pageSize, int unreadCount)
        : base(items, count, pageNumber, pageSize)
    {
        UnreadCount = unreadCount;
    }
}

/// <summary>Result of marking one notification, or all of the caller's notifications, as read.</summary>
public class MarkNotificationsReadResponse
{
    /// <summary>How many rows this call actually flipped from unread to read.</summary>
    public int MarkedCount { get; set; }

    /// <summary>The caller's remaining unread count after the call - the new badge value.</summary>
    public int UnreadCount { get; set; }
}
