using System.Diagnostics;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.System;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface ISystemHealthService
{
    Task<SystemHealthDto> GetSystemHealthAsync(CancellationToken cancellationToken = default);
}

public class SystemHealthService : ISystemHealthService
{
    private readonly IApplicationDbContext _context;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public SystemHealthService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SystemHealthDto> GetSystemHealthAsync(CancellationToken cancellationToken = default)
    {
        var dbOk = true;
        try
        {
            await _context.SystemSettings.Take(1).ToListAsync(cancellationToken);
        }
        catch
        {
            dbOk = false;
        }

        var pendingOutbox = await _context.OutboxMessages.CountAsync(m => m.Status == "Pending" || m.ProcessedOnUtc == null, cancellationToken);
        var failedOutbox = await _context.OutboxMessages.CountAsync(m => m.Status == "Failed", cancellationToken);
        var deadLetterOutbox = await _context.OutboxMessages.CountAsync(m => m.Status == "DeadLetter", cancellationToken);

        var currentProcess = Process.GetCurrentProcess();
        var memoryMb = currentProcess.WorkingSet64 / (1024 * 1024);
        var uptimeSpan = DateTime.UtcNow - _startTime;

        return new SystemHealthDto
        {
            Status = dbOk && deadLetterOutbox == 0 ? "Healthy" : (dbOk ? "Degraded" : "Unhealthy"),
            DatabaseStatus = dbOk ? "Connected" : "Disconnected",
            OutboxPendingCount = pendingOutbox,
            OutboxFailedCount = failedOutbox,
            OutboxDeadLetterCount = deadLetterOutbox,
            ProcessMemoryMb = memoryMb,
            Uptime = $"{(int)uptimeSpan.TotalDays}d {uptimeSpan.Hours}h {uptimeSpan.Minutes}m {uptimeSpan.Seconds}s",
            ServerTimeUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
    }
}
