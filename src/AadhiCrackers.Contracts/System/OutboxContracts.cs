namespace AadhiCrackers.Contracts.System;

/// <summary>
/// One outbox message, as shown to an administrator investigating undelivered events.
/// <see cref="PayloadJson"/> is included because it is usually the only record of what the event
/// was raised for once the order row has moved on.
/// </summary>
public class OutboxMessageDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

/// <summary>
/// Puts undelivered messages back in the queue.
///
/// Supply <see cref="Ids"/> to requeue specific messages, or <see cref="Type"/> to requeue every
/// stuck message of one event type (the usual case: a handler was missing, a release added it,
/// and everything of that type now needs another run). Supplying neither requeues every stuck
/// message, which the endpoint requires <see cref="ConfirmAll"/> for.
/// </summary>
public class RequeueOutboxRequest
{
    public List<Guid>? Ids { get; set; }

    public string? Type { get; set; }

    /// <summary>Required when neither Ids nor Type is given, so "requeue everything" is deliberate.</summary>
    public bool ConfirmAll { get; set; }
}

public class RequeueOutboxResultDto
{
    public int RequeuedCount { get; set; }

    /// <summary>Ids that were asked for but skipped, with the reason — usually already delivered.</summary>
    public Dictionary<string, string> Skipped { get; set; } = new();
}
