using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Audit;

public class AuditLogDto
{
    public Guid Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public AuditAction Action { get; set; }
    public string ActionName => Action.ToString();
    public string Module { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public AuditSeverity Severity { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
}

public class AuditLogDetailDto : AuditLogDto
{
    public string? UserAgent { get; set; }
    public string? ClientApplication { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? ChangedFieldsJson { get; set; }
    public string? MetadataJson { get; set; }
}

public class AuditLogFilterRequest
{
    public string? Search { get; set; }
    public string? UserId { get; set; }
    public AuditAction? Action { get; set; }
    public string? Module { get; set; }
    public string? EntityType { get; set; }
    public AuditSeverity? Severity { get; set; }
    public bool? Success { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime? FromDateUtc { get; set; }
    public DateTime? ToDateUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class SystemSettingDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEncrypted { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class UpdateSettingRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class RateLimitLogDto
{
    public string IpAddress { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public DateTime BlockedAtUtc { get; set; }
    public int RejectionCount { get; set; }
    public string Reason { get; set; } = "Exceeded rate limit policy quota";
}
