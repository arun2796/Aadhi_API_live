using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.System;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IOutboxAdminService
{
    Task<List<OutboxMessageDto>> GetUndeliveredAsync(string? status = null, int limit = 100, CancellationToken cancellationToken = default);

    Task<RequeueOutboxResultDto> RequeueAsync(RequeueOutboxRequest request, CancellationToken cancellationToken = default);
}

public class OutboxAdminService : IOutboxAdminService
{
    private const int MaxLimit = 500;

    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public OutboxAdminService(IApplicationDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<List<OutboxMessageDto>> GetUndeliveredAsync(
        string? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        var query = _context.OutboxMessages.Where(m => m.ProcessedOnUtc == null);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            query = query.Where(m => m.Status == wanted);
        }

        return await query
            .OrderBy(m => m.OccurredOnUtc)
            .Take(limit)
            .Select(m => new OutboxMessageDto
            {
                Id = m.Id,
                Type = m.Type,
                Status = m.Status,
                OccurredOnUtc = m.OccurredOnUtc,
                ProcessedOnUtc = m.ProcessedOnUtc,
                RetryCount = m.RetryCount,
                Error = m.Error,
                PayloadJson = m.PayloadJson
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RequeueOutboxResultDto> RequeueAsync(
        RequeueOutboxRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new RequeueOutboxResultDto();

        var hasIds = request.Ids is { Count: > 0 };
        var hasType = !string.IsNullOrWhiteSpace(request.Type);

        if (!hasIds && !hasType && !request.ConfirmAll)
        {
            throw new DomainException(
                "Specify ids, a type, or set confirmAll to requeue every undelivered message. " +
                "Requeuing everything is not the default because each message sends a real " +
                "notification to a real customer.");
        }

        var query = _context.OutboxMessages.Where(m => m.ProcessedOnUtc == null);

        if (hasIds)
        {
            var ids = request.Ids!.Distinct().ToList();
            var found = await _context.OutboxMessages
                .Where(m => ids.Contains(m.Id))
                .ToListAsync(cancellationToken);

            foreach (var id in ids)
            {
                var match = found.FirstOrDefault(m => m.Id == id);
                if (match is null)
                {
                    result.Skipped[id.ToString()] = "No such outbox message.";
                }
                else if (match.ProcessedOnUtc != null)
                {
                    result.Skipped[id.ToString()] = "Already delivered — requeuing would send it a second time.";
                }
            }

            query = query.Where(m => ids.Contains(m.Id));
        }
        else if (hasType)
        {
            var type = request.Type!.Trim();
            query = query.Where(m => m.Type == type);
        }

        var messages = await query.ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = "Pending";
            message.RetryCount = 0;
            message.Error = null;
            message.NextAttemptAtUtc = null;
        }

        result.RequeuedCount = messages.Count;

        if (messages.Count > 0)
        {
            await _auditLog.LogAsync(
                AuditAction.Update,
                "System",
                nameof(Domain.Entities.OutboxMessage),
                entityName: $"Requeued {messages.Count} outbox message(s)",
                metadata: new
                {
                    RequeuedCount = messages.Count,
                    request.Type,
                    RequestedIds = request.Ids,
                    Types = messages.Select(m => m.Type).Distinct().ToList()
                },
                severity: AuditSeverity.Warning);

            await _context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
