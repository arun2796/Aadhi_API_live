namespace AadhiCrackers.Contracts.System;

public class SystemHealthDto
{
    public string Status { get; set; } = "Healthy";
    public string DatabaseStatus { get; set; } = "Connected";
    public int OutboxPendingCount { get; set; }
    public int OutboxFailedCount { get; set; }
    public int OutboxDeadLetterCount { get; set; }
    public long ProcessMemoryMb { get; set; }
    public string Uptime { get; set; } = string.Empty;
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
}
