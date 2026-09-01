using System.Text.Json;
using System.Text.RegularExpressions;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Audit;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IAuditLogService
{
    Task LogAsync(
        AuditAction action,
        string module,
        string entityType,
        string? entityId = null,
        string? entityName = null,
        object? before = null,
        object? after = null,
        object? changedFields = null,
        object? metadata = null,
        AuditSeverity severity = AuditSeverity.Info,
        bool success = true,
        string? failureReason = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogFilterRequest filter, CancellationToken cancellationToken = default);
    Task<AuditLogDetailDto?> GetAuditLogByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public class AuditLogService : IAuditLogService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOutboxService _outbox;

    private static readonly Regex SensitiveKeyRegex = new(
        @"(password|token|secret|authorization|cardnumber|cvv|accountkey)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AuditLogService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IOutboxService outbox)
    {
        _context = context;
        _currentUser = currentUser;
        _outbox = outbox;
    }

    public async Task LogAsync(
        AuditAction action,
        string module,
        string entityType,
        string? entityId = null,
        string? entityName = null,
        object? before = null,
        object? after = null,
        object? changedFields = null,
        object? metadata = null,
        AuditSeverity severity = AuditSeverity.Info,
        bool success = true,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var audit = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName ?? _currentUser.Email ?? "Anonymous",
            Role = _currentUser.Role,
            Action = action,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            HttpMethod = "INTERNAL",
            RequestPath = string.Empty,
            CorrelationId = _currentUser.CorrelationId,
            IpAddress = _currentUser.IpAddress,
            UserAgent = _currentUser.UserAgent,
            Severity = severity,
            Success = success,
            FailureReason = failureReason,
            BeforeJson = MaskJson(Serialize(before)),
            AfterJson = MaskJson(Serialize(after)),
            ChangedFieldsJson = MaskJson(Serialize(changedFields)),
            MetadataJson = MaskJson(Serialize(metadata))
        };

        _context.AuditLogs.Add(audit);

        // Also queue outbox message for asynchronous projection to Elasticsearch
        await _outbox.EnqueueAsync("AuditLogCreated", new
        {
            AuditLogId = audit.Id,
            audit.TimestampUtc,
            audit.UserId,
            audit.UserName,
            audit.Role,
            Action = audit.Action.ToString(),
            audit.Module,
            audit.EntityType,
            audit.EntityId,
            audit.EntityName,
            audit.CorrelationId,
            audit.IpAddress,
            Severity = audit.Severity.ToString(),
            audit.Success
        }, cancellationToken);

        // Persistence is deliberately owned by the calling command transaction. This keeps
        // the business mutation, audit entry, and outbox message atomic.
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(a =>
                (a.EntityName != null && a.EntityName.ToLower().Contains(s)) ||
                (a.UserName != null && a.UserName.ToLower().Contains(s)) ||
                (a.CorrelationId != null && a.CorrelationId.ToLower().Contains(s)) ||
                (a.Module != null && a.Module.ToLower().Contains(s)) ||
                (a.EntityType != null && a.EntityType.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
            query = query.Where(a => a.UserId == filter.UserId);

        if (filter.Action.HasValue)
            query = query.Where(a => a.Action == filter.Action.Value);

        if (!string.IsNullOrWhiteSpace(filter.Module))
            query = query.Where(a => a.Module == filter.Module);

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.EntityType == filter.EntityType);

        if (filter.Severity.HasValue)
            query = query.Where(a => a.Severity == filter.Severity.Value);

        if (filter.Success.HasValue)
            query = query.Where(a => a.Success == filter.Success.Value);

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
            query = query.Where(a => a.CorrelationId == filter.CorrelationId);

        if (filter.FromDateUtc.HasValue)
            query = query.Where(a => a.TimestampUtc >= filter.FromDateUtc.Value);

        if (filter.ToDateUtc.HasValue)
            query = query.Where(a => a.TimestampUtc <= filter.ToDateUtc.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                TimestampUtc = a.TimestampUtc,
                UserId = a.UserId,
                UserName = a.UserName,
                Role = a.Role,
                Action = a.Action,
                Module = a.Module,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                EntityName = a.EntityName,
                HttpMethod = a.HttpMethod,
                RequestPath = a.RequestPath,
                CorrelationId = a.CorrelationId,
                TraceId = a.TraceId,
                IpAddress = a.IpAddress,
                Severity = a.Severity,
                Success = a.Success,
                FailureReason = a.FailureReason
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<AuditLogDetailDto?> GetAuditLogByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var a = await _context.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (a == null) return null;

        return new AuditLogDetailDto
        {
            Id = a.Id,
            TimestampUtc = a.TimestampUtc,
            UserId = a.UserId,
            UserName = a.UserName,
            Role = a.Role,
            Action = a.Action,
            Module = a.Module,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            EntityName = a.EntityName,
            HttpMethod = a.HttpMethod,
            RequestPath = a.RequestPath,
            CorrelationId = a.CorrelationId,
            TraceId = a.TraceId,
            IpAddress = a.IpAddress,
            Severity = a.Severity,
            Success = a.Success,
            FailureReason = a.FailureReason,
            UserAgent = a.UserAgent,
            ClientApplication = a.ClientApplication,
            BeforeJson = a.BeforeJson,
            AfterJson = a.AfterJson,
            ChangedFieldsJson = a.ChangedFieldsJson,
            MetadataJson = a.MetadataJson
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        WriteIndented = false
    };

    private static string? Serialize(object? obj) =>
        obj is null ? null : (obj is string str ? str : JsonSerializer.Serialize(obj, JsonOptions));

    private static string? MaskJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        try
        {
            // Mask any key matching sensitive keys in json: "password": "...", "token": "..."
            return Regex.Replace(json, @"(?i)""(password|token|secret|authorization|cardnumber|cvv|accountkey)""\s*:\s*""([^""]+)""",
                @"""$1"": ""***""");
        }
        catch
        {
            return json;
        }
    }
}
