using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Domain.Entities;

public class AuditLog : BaseEntity<Guid>
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public AuditAction Action { get; set; }
    public string Module { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientApplication { get; set; }
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;
    public bool Success { get; set; } = true;
    public string? FailureReason { get; set; }

    // JSON fields (Masked!)
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? ChangedFieldsJson { get; set; }
    public string? MetadataJson { get; set; }
}

public class OutboxMessage : BaseEntity<Guid>
{
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Processing, Processed, Failed, DeadLetter
    public DateTime? NextAttemptAtUtc { get; set; }
}

public class SystemSetting : BaseEntity<Guid>
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Group { get; set; } = "General"; // General, Store, Tax, Shipping, Notification, Security, Elastic
    public string? Description { get; set; }
    public bool IsEncrypted { get; set; }
}

public class OtpVerification : BaseEntity<Guid>
{
    public string UserId { get; set; } = string.Empty; // ASP.NET Identity User ID
    public string Code { get; set; } = string.Empty; // 6-digit OTP
    public string Purpose { get; set; } = "PasswordReset";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiresAtUtc { get; set; }
}

public class LoginHistory : BaseEntity<Guid>
{
    public string? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; } = true;
    public string? FailureReason { get; set; }
}

public class RateLimitLog : BaseEntity<Guid>
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Endpoint { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int RequestsCount { get; set; }
    public int BlockedCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
