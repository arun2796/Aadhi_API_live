using System.Text.Json;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Notifications;
using AadhiCrackers.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface INotificationQueryService
{
    /// <summary>The signed-in customer's own notifications, newest first, paged, with an unread count.</summary>
    Task<NotificationListResponse> GetMyNotificationsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every notification raised for ONE order, newest first. Anonymous: the order number is the
    /// handle, exactly as it is for GET /orders/track/{orderNumber}.
    /// </summary>
    Task<List<NotificationDto>> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Marks one of the caller's own notifications read. Null when it is not theirs / does not exist.</summary>
    Task<MarkNotificationsReadResponse?> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification belonging to the caller as read.</summary>
    Task<MarkNotificationsReadResponse> MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read/flag side of the in-house notification feature. Writing rows is the job of
/// INotificationService (Infrastructure), which the outbox already drives.
///
/// GUEST ISOLATION. Every guest checkout is attached to one shared customer record
/// (OrderPricingService.GuestCustomerEmail), so "all notifications of my customer id" would be a
/// leak if that shared record were ever treated as an owner. Three independent guards:
///   1. Rows for guest orders are written with CustomerId = NULL (see NotificationService), so they
///      cannot match ANY per-customer predicate.
///   2. Every per-customer query below filters on a concrete, non-null CustomerId equal to the
///      caller's own resolved customer id. A null CustomerId never equals anything in SQL.
///   3. If the caller somehow authenticates AS the shared guest record, the query is refused
///      outright and an empty page is returned (IsSharedGuestBucket below).
/// The anonymous per-order endpoint filters on the order number alone and therefore returns only
/// that one order's rows.
/// </summary>
public class NotificationQueryService : INotificationQueryService
{
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public NotificationQueryService(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<NotificationListResponse> GetMyNotificationsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

        var customerId = await ResolveOwnCustomerIdAsync(cancellationToken);
        if (customerId == null)
        {
            return new NotificationListResponse(new List<NotificationDto>(), 0, page, pageSize, 0);
        }

        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var unreadCount = await query.CountAsync(n => !n.IsRead, cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new NotificationListResponse(items.Select(MapToDto).ToList(), totalCount, page, pageSize, unreadCount);
    }

    public async Task<List<NotificationDto>> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return new List<NotificationDto>();
        }

        // Order numbers are generated and stored upper-case (ORD-YYYY-NNNNNN), so upper-casing the
        // INPUT - exactly what GET /orders/track/{orderNumber} does - accepts the same values there
        // while keeping the column bare, so IX_Notifications_OrderNumber_CreatedAtUtc stays usable.
        var normalized = orderNumber.Trim().ToUpper();

        var items = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.OrderNumber == normalized)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .ToListAsync(cancellationToken);

        return items.Select(MapToDto).ToList();
    }

    public async Task<MarkNotificationsReadResponse?> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var customerId = await ResolveOwnCustomerIdAsync(cancellationToken);
        if (customerId == null)
        {
            return null;
        }

        // Ownership is part of the predicate, not an afterthought: a notification that is not the
        // caller's simply is not found, so the endpoint cannot be used to probe for other people's rows.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.CustomerId == customerId.Value, cancellationToken);

        if (notification == null)
        {
            return null;
        }

        var marked = 0;
        if (!notification.IsRead)
        {
            notification.MarkRead();
            marked = 1;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MarkNotificationsReadResponse
        {
            MarkedCount = marked,
            UnreadCount = await CountUnreadAsync(customerId.Value, cancellationToken)
        };
    }

    public async Task<MarkNotificationsReadResponse> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var customerId = await ResolveOwnCustomerIdAsync(cancellationToken);
        if (customerId == null)
        {
            return new MarkNotificationsReadResponse { MarkedCount = 0, UnreadCount = 0 };
        }

        var unread = await _context.Notifications
            .Where(n => n.CustomerId == customerId.Value && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.MarkRead();
        }

        if (unread.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MarkNotificationsReadResponse
        {
            MarkedCount = unread.Count,
            UnreadCount = await CountUnreadAsync(customerId.Value, cancellationToken)
        };
    }

    private Task<int> CountUnreadAsync(Guid customerId, CancellationToken cancellationToken) =>
        _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.CustomerId == customerId && !n.IsRead, cancellationToken);

    /// <summary>
    /// The caller's own customer record, or null when there is none - or when the caller resolves to
    /// the SHARED GUEST BUCKET, which has no single owner and must never be served as "mine".
    /// </summary>
    private async Task<Guid?> ResolveOwnCustomerIdAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var email = _currentUser.Email;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email)) return null;

        if (OrderPricingService.IsSharedGuestBucket(email))
        {
            return null;
        }

        var customer = await _context.Customers
            .AsNoTracking()
            .Where(c =>
                (userId != null && c.UserId == userId) ||
                (email != null && c.Email.ToLower() == email.ToLower()))
            .Select(c => new { c.Id, c.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer == null || OrderPricingService.IsSharedGuestBucket(customer.Email))
        {
            return null;
        }

        return customer.Id;
    }

    private static NotificationDto MapToDto(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Message = n.Message,
        OrderId = n.OrderId,
        OrderNumber = n.OrderNumber,
        IsRead = n.IsRead,
        CreatedAtUtc = n.CreatedAtUtc,
        ReadAtUtc = n.ReadAtUtc,
        OrderStatus = n.OrderStatus,
        CarrierName = n.CarrierName,
        TrackingNumber = n.TrackingNumber,
        CarrierPhone = n.CarrierPhone,
        CarrierAddress = n.CarrierAddress,
        Data = DeserializeData(n.DataJson)
    };

    private static Dictionary<string, string>? DeserializeData(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(dataJson);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            // A malformed bag must never take down the notification list.
            return null;
        }
    }
}
