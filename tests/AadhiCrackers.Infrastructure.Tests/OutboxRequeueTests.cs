using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.System;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Infrastructure.Tests;

public class OutboxRequeueTests
{
    [Fact]
    public async Task RequeueByType_MakesADeadLetteredMessageVisibleToTheProcessorAgain()
    {
        await using var context = NewContext();
        var stuck = AddMessage(context, "OrderDispatched", status: "DeadLetter", retryCount: 5,
            error: "Outbox event type 'OrderDispatched' has no registered handler and cannot be processed.",
            nextAttempt: DateTime.UtcNow.AddMinutes(-5));
        await context.SaveChangesAsync();

        var result = await NewService(context).RequeueAsync(new RequeueOutboxRequest { Type = "OrderDispatched" });

        result.RequeuedCount.Should().Be(1);

        var reloaded = await context.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == stuck.Id);
        reloaded.Status.Should().Be("Pending");
        reloaded.RetryCount.Should().Be(0);
        reloaded.Error.Should().BeNull();
        reloaded.NextAttemptAtUtc.Should().BeNull();

        // The real assertion: it now matches the processor's polling predicate, which is the only
        // thing deciding whether the customer ever gets their notification.
        // Not ContainSingle — the requeue is itself audited, and AuditLogService enqueues its own
        // AuditLogCreated message, so a second pending row is expected here.
        var now = DateTime.UtcNow;
        var visibleIds = await context.OutboxMessages
            .Where(m => (m.Status == "Pending" || m.Status == "Failed")
                        && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)
                        && m.RetryCount < 5)
            .Select(m => m.Id)
            .ToListAsync();

        visibleIds.Should().Contain(stuck.Id);
    }

    [Fact]
    public async Task Requeue_NeverTouchesAnAlreadyDeliveredMessage()
    {
        await using var context = NewContext();
        var delivered = AddMessage(context, "OrderDispatched", status: "Processed", retryCount: 0);
        delivered.ProcessedOnUtc = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        var result = await NewService(context).RequeueAsync(new RequeueOutboxRequest { Type = "OrderDispatched" });

        // Resending "your order has shipped" to a customer who got it yesterday cannot be undone.
        result.RequeuedCount.Should().Be(0);

        var reloaded = await context.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == delivered.Id);
        reloaded.Status.Should().Be("Processed");
        reloaded.ProcessedOnUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RequeueByIds_ReportsDeliveredAndUnknownIdsAsSkipped()
    {
        await using var context = NewContext();
        var stuck = AddMessage(context, "PaymentVerified", status: "DeadLetter", retryCount: 5);
        var delivered = AddMessage(context, "OrderPlaced", status: "Processed", retryCount: 0);
        delivered.ProcessedOnUtc = DateTime.UtcNow;
        var unknownId = Guid.NewGuid();
        await context.SaveChangesAsync();

        var result = await NewService(context).RequeueAsync(new RequeueOutboxRequest
        {
            Ids = new List<Guid> { stuck.Id, delivered.Id, unknownId }
        });

        result.RequeuedCount.Should().Be(1);
        result.Skipped.Should().ContainKey(delivered.Id.ToString())
            .WhoseValue.Should().Contain("Already delivered");
        result.Skipped.Should().ContainKey(unknownId.ToString());
    }

    [Fact]
    public async Task RequeueEverything_RequiresAnExplicitConfirmation()
    {
        await using var context = NewContext();
        AddMessage(context, "OrderDispatched", status: "DeadLetter", retryCount: 5);
        await context.SaveChangesAsync();

        var service = NewService(context);

        var act = () => service.RequeueAsync(new RequeueOutboxRequest());
        await act.Should().ThrowAsync<DomainException>();

        // Same call, deliberately confirmed.
        var confirmed = await service.RequeueAsync(new RequeueOutboxRequest { ConfirmAll = true });
        confirmed.RequeuedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetUndelivered_ListsStuckMessagesAndExcludesDeliveredOnes()
    {
        await using var context = NewContext();
        AddMessage(context, "OrderDispatched", status: "DeadLetter", retryCount: 5);
        AddMessage(context, "OrderStatusChanged", status: "Failed", retryCount: 2);
        var delivered = AddMessage(context, "OrderPlaced", status: "Processed", retryCount: 0);
        delivered.ProcessedOnUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var service = NewService(context);

        (await service.GetUndeliveredAsync()).Should().HaveCount(2);
        (await service.GetUndeliveredAsync(status: "DeadLetter"))
            .Should().ContainSingle().Which.Type.Should().Be("OrderDispatched");
    }

    // ---------------------------------------------------------------- helpers

    private static OutboxMessage AddMessage(
        AadhiDbContext context,
        string type,
        string status,
        int retryCount,
        string? error = null,
        DateTime? nextAttempt = null)
    {
        var message = new OutboxMessage
        {
            Type = type,
            Status = status,
            RetryCount = retryCount,
            Error = error,
            NextAttemptAtUtc = nextAttempt,
            PayloadJson = $$"""{"OrderId":"{{Guid.NewGuid()}}"}""",
            OccurredOnUtc = DateTime.UtcNow.AddHours(-1)
        };

        context.OutboxMessages.Add(message);
        return message;
    }

    private static OutboxAdminService NewService(AadhiDbContext context)
    {
        var user = new TestCurrentUser();
        return new OutboxAdminService(context, new AuditLogService(context, user, new OutboxService(context)));
    }

    private static AadhiDbContext NewContext()
    {
        // A shared in-memory SQLite connection: relational behaviour, no file to clean up.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();

        var context = new AadhiDbContext(
            new DbContextOptionsBuilder<AadhiDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-admin-id";
        public string? Email => "admin@aadhicracker.in";
        public string? UserName => "admin";
        public string? Role => "SuperAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "TestRunner";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }
}
