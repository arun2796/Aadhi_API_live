using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

/// <summary>
/// The customer's own notification inbox, served from the Notifications table this API writes to on
/// every order lifecycle event. No third-party gateway is involved anywhere in this feature.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationQueryService _notifications;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(INotificationQueryService notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The signed-in customer's own notifications, newest first, paged, with the unread badge count.
    /// Only rows whose CustomerId is the caller's own customer id are ever returned; guest-order
    /// notifications have no owner at all and can never appear here.
    /// </summary>
    [HttpGet]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Notifications)]
    public async Task<ActionResult<ApiResponse<NotificationListResponse>>> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.GetMyNotificationsAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<NotificationListResponse>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    /// <summary>
    /// Every notification raised for ONE order. Anonymous on purpose, with the same security posture
    /// as GET /orders/track/{orderNumber}: guests check out without an account, so the order number
    /// is the only handle they have. It returns strictly less about the order than that endpoint
    /// already does, and only the rows carrying this exact order number.
    /// </summary>
    [HttpGet("order/{orderNumber}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> GetByOrderNumber(string orderNumber, CancellationToken cancellationToken)
    {
        var items = await _notifications.GetByOrderNumberAsync(orderNumber, cancellationToken);
        return Ok(ApiResponse<List<NotificationDto>>.Ok(items, correlationId: _currentUser.CorrelationId));
    }

    /// <summary>Marks one of the caller's own notifications as read.</summary>
    [HttpPost("{id:guid}/read")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Notifications)]
    public async Task<ActionResult<ApiResponse<MarkNotificationsReadResponse>>> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await _notifications.MarkAsReadAsync(id, cancellationToken);
        if (result == null)
        {
            // A notification that is not the caller's is reported as missing rather than forbidden,
            // so this endpoint cannot be used to discover other people's notification ids.
            return NotFound(ApiResponse<MarkNotificationsReadResponse>.Fail($"Notification '{id}' not found", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<MarkNotificationsReadResponse>.Ok(result, "Notification marked as read", _currentUser.CorrelationId));
    }

    /// <summary>Marks every unread notification belonging to the caller as read.</summary>
    [HttpPost("read-all")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Notifications)]
    public async Task<ActionResult<ApiResponse<MarkNotificationsReadResponse>>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var result = await _notifications.MarkAllAsReadAsync(cancellationToken);
        return Ok(ApiResponse<MarkNotificationsReadResponse>.Ok(result, "All notifications marked as read", _currentUser.CorrelationId));
    }
}
