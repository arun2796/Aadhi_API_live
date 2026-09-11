using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Domain.Entities;

/// <summary>
/// A customer-facing message about something that happened to an order, persisted in OUR OWN table
/// and served back to the app. There is deliberately no SMS / WhatsApp / e-mail gateway involved:
/// the shop tells the customer what is happening by writing a row here, and the storefront reads it.
///
/// The single most important row this table ever holds is the dispatch notification, because the
/// consignment travels by lorry to a transport office and the customer has to know WHICH transport
/// company took it, the LR / waybill number to quote, and the office phone number and address to
/// walk into. Those four facts therefore live in real columns as well as inside the rendered
/// message, so the UI can lay them out (tap-to-call the phone, copy the LR, map the address)
/// without ever having to parse English prose back apart.
/// </summary>
public class Notification : BaseEntity<Guid>
{
    /// <summary>
    /// Owning customer, or NULL when the notification has no single owner that a per-customer query
    /// may serve. Guest checkouts all share one customer record (guest@aadhicracker.in), so a
    /// guest order's notification is stored with CustomerId = NULL on purpose: that makes it
    /// structurally impossible for "my notifications" to hand one guest another guest's messages.
    /// Such a row is reachable only through its order number. See NotificationService.
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>The order this concerns, when it concerns one.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// The human order number (ORD-YYYY-NNNNNN), denormalised so the anonymous per-order lookup and
    /// the list rendering never need a join, and so the row still reads correctly if the order is
    /// ever soft-deleted.
    /// </summary>
    public string? OrderNumber { get; set; }

    /// <summary>Event discriminator; persisted as its NAME, not its ordinal (see AadhiDbContext).</summary>
    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    /// <summary>Order status at the moment the notification was raised, e.g. "Shipped".</summary>
    public string? OrderStatus { get; set; }

    // ---- Structured dispatch facts (the whole point of the feature) ----
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CarrierPhone { get; set; }
    public string? CarrierAddress { get; set; }

    /// <summary>
    /// Small flat JSON bag of string values for the long tail (rejection reason, order total,
    /// previous status...). Anything the UI must lay out on its own gets a real column above;
    /// this exists so a new event type does not need a migration for one extra caption.
    /// </summary>
    public string? DataJson { get; set; }

    /// <summary>
    /// Idempotency handle, unique across the table. The outbox retries and can redeliver a message,
    /// so every notification is derived from a key that identifies the LOGICAL event
    /// (e.g. "OrderDispatched|{orderId}|{lrNumber}") rather than the delivery attempt.
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ReadAtUtc;
    }
}
