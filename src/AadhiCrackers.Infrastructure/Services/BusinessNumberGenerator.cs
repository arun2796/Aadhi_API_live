using AadhiCrackers.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Infrastructure.Services;

public class BusinessNumberGenerator : IBusinessNumberGenerator
{
    private readonly IApplicationDbContext _context;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public BusinessNumberGenerator(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"ORD-{year}-";
        return await GenerateNextNumberAsync(
            _context.Orders.Where(o => o.OrderNumber.StartsWith(prefix)).Select(o => o.OrderNumber),
            prefix,
            cancellationToken);
    }

    public async Task<string> GenerateInvoiceNumberAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"INV-{year}-";
        return await GenerateNextNumberAsync(
            _context.Invoices.Where(i => i.InvoiceNumber.StartsWith(prefix)).Select(i => i.InvoiceNumber),
            prefix,
            cancellationToken);
    }

    public async Task<string> GeneratePaymentNumberAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"PAY-{year}-";
        return await GenerateNextNumberAsync(
            _context.Payments.Where(p => p.PaymentNumber.StartsWith(prefix)).Select(p => p.PaymentNumber),
            prefix,
            cancellationToken);
    }

    public async Task<string> GenerateExpenseNumberAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"EXP-{year}-";
        return await GenerateNextNumberAsync(
            _context.Expenses.Where(e => e.ExpenseNumber.StartsWith(prefix)).Select(e => e.ExpenseNumber),
            prefix,
            cancellationToken);
    }

    private static async Task<string> GenerateNextNumberAsync(
        IQueryable<string> existingNumbersQuery,
        string prefix,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var existingNumbers = await existingNumbersQuery.ToListAsync(cancellationToken);
            var maxSeq = 0;

            foreach (var num in existingNumbers)
            {
                if (num.Length > prefix.Length && int.TryParse(num[prefix.Length..], out var parsedSeq))
                {
                    if (parsedSeq > maxSeq)
                    {
                        maxSeq = parsedSeq;
                    }
                }
            }

            var nextSeq = maxSeq + 1;
            return $"{prefix}{nextSeq:D6}";
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
