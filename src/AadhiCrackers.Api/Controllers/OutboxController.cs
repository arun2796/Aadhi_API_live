using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AadhiCrackers.Api.Controllers;

/// <summary>
/// Inspecting and replaying undelivered outbox events.
///
/// The System Health screen already reports how many messages are stuck; until now there was no
/// way to act on that number from anywhere but a psql prompt. Every event here is a customer
/// notification that was raised and never delivered.
/// </summary>
[ApiController]
[Route("api/v1/admin/outbox")]
[Authorize(Policy = "RequireAdmin")]
public class OutboxController : ControllerBase
{
    private readonly IOutboxAdminService _outbox;
    private readonly ICurrentUserService _currentUser;

    public OutboxController(IOutboxAdminService outbox, ICurrentUserService currentUser)
    {
        _outbox = outbox;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Undelivered messages, oldest first. <paramref name="status"/> narrows to one state —
    /// "DeadLetter" for the ones that have given up, "Failed" for those still retrying.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<OutboxMessageDto>>>> GetUndelivered(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var messages = await _outbox.GetUndeliveredAsync(status, limit, cancellationToken);
        return Ok(ApiResponse<List<OutboxMessageDto>>.Ok(messages, correlationId: _currentUser.CorrelationId));
    }

    /// <summary>
    /// Puts undelivered messages back in the queue; the background processor picks them up on its
    /// next pass. Messages that have already been delivered are never touched — they are reported
    /// in <c>skipped</c> instead, because resending is not undoable.
    /// </summary>
    [HttpPost("requeue")]
    public async Task<ActionResult<ApiResponse<RequeueOutboxResultDto>>> Requeue(
        [FromBody] RequeueOutboxRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _outbox.RequeueAsync(request, cancellationToken);

        var message = result.RequeuedCount == 0
            ? "Nothing to requeue."
            : $"{result.RequeuedCount} message(s) requeued; they will be sent within a minute.";

        return Ok(ApiResponse<RequeueOutboxResultDto>.Ok(result, message, _currentUser.CorrelationId));
    }
}
