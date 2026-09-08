using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IFinanceService
{
    Task<PagedResult<InvoiceDto>> GetInvoicesAsync(int page = 1, int pageSize = 20, InvoiceStatus? status = null, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentDto>> GetPaymentsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<PaymentDto?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    // Expenses & P&L
    Task<PagedResult<ExpenseDto>> GetExpensesAsync(int page = 1, int pageSize = 20, ExpenseCategory? category = null, CancellationToken cancellationToken = default);
    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ProfitLossDto> GetProfitLossAsync(DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default);
}

public class FinanceService : IFinanceService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IOutboxService _outbox;
    private readonly IBusinessNumberGenerator _numberGenerator;

    public FinanceService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IOutboxService outbox,
        IBusinessNumberGenerator numberGenerator)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _outbox = outbox;
        _numberGenerator = numberGenerator;
    }

    public async Task<PagedResult<InvoiceDto>> GetInvoicesAsync(int page = 1, int pageSize = 20, InvoiceStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Invoices
            .AsNoTracking()
            .Include(i => i.Order)
            .Include(i => i.Customer)
            .Where(i => !i.IsDeleted);

        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => i.IssuedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InvoiceDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                OrderId = i.OrderId,
                OrderNumber = i.Order.OrderNumber,
                CustomerId = i.CustomerId,
                CustomerName = $"{i.Customer.FirstName} {i.Customer.LastName}".Trim(),
                Subtotal = i.Subtotal.ToDecimal(),
                Discount = i.Discount.ToDecimal(),
                Tax = i.Tax.ToDecimal(),
                Shipping = i.Shipping.ToDecimal(),
                GrandTotal = i.GrandTotal.ToDecimal(),
                PaidAmount = i.PaidAmount.ToDecimal(),
                BalanceAmount = i.BalanceAmount.ToDecimal(),
                Status = i.Status,
                IssuedAtUtc = i.IssuedAtUtc,
                DueDateUtc = i.DueDateUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<InvoiceDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<PaymentDto>> GetPaymentsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Order)
            .Include(p => p.Customer)
            .Where(p => !p.IsDeleted);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.PaidAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                PaymentNumber = p.PaymentNumber,
                OrderId = p.OrderId,
                OrderNumber = p.Order != null ? p.Order.OrderNumber : null,
                CustomerId = p.CustomerId,
                CustomerName = $"{p.Customer.FirstName} {p.Customer.LastName}".Trim(),
                Amount = p.Amount.ToDecimal(),
                PaymentMethod = p.PaymentMethod,
                PaymentStatus = p.PaymentStatus,
                TransactionReference = p.TransactionReference,
                UtrNumber = p.UtrNumber,
                IdempotencyKey = p.IdempotencyKey,
                Notes = p.Notes,
                PaidAtUtc = p.PaidAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>(items, totalCount, page, pageSize);
    }

    public async Task<PaymentDto?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var p = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Order)
            .Include(p => p.Customer)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (p == null) return null;

        return new PaymentDto
        {
            Id = p.Id,
            PaymentNumber = p.PaymentNumber,
            OrderId = p.OrderId,
            OrderNumber = p.Order?.OrderNumber,
            CustomerId = p.CustomerId,
            CustomerName = $"{p.Customer.FirstName} {p.Customer.LastName}".Trim(),
            Amount = p.Amount.ToDecimal(),
            PaymentMethod = p.PaymentMethod,
            PaymentStatus = p.PaymentStatus,
            TransactionReference = p.TransactionReference,
            UtrNumber = p.UtrNumber,
            IdempotencyKey = p.IdempotencyKey,
            Notes = p.Notes,
            PaidAtUtc = p.PaidAtUtc
        };
    }

    public async Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new DomainException("Payment amount must be greater than zero.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingPayment = await _context.Payments
                .Include(p => p.Order)
                .Include(p => p.Customer)
                .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey && !p.IsDeleted, cancellationToken);

            if (existingPayment != null)
            {
                return new PaymentDto
                {
                    Id = existingPayment.Id,
                    PaymentNumber = existingPayment.PaymentNumber,
                    OrderId = existingPayment.OrderId,
                    OrderNumber = existingPayment.Order?.OrderNumber,
                    CustomerId = existingPayment.CustomerId,
                    CustomerName = $"{existingPayment.Customer.FirstName} {existingPayment.Customer.LastName}".Trim(),
                    Amount = existingPayment.Amount.ToDecimal(),
                    PaymentMethod = existingPayment.PaymentMethod,
                    PaymentStatus = existingPayment.PaymentStatus,
                    TransactionReference = existingPayment.TransactionReference,
                    UtrNumber = existingPayment.UtrNumber,
                    IdempotencyKey = existingPayment.IdempotencyKey,
                    Notes = existingPayment.Notes,
                    PaidAtUtc = existingPayment.PaidAtUtc
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.UtrNumber))
        {
            var utrExists = await _context.Payments
                .AnyAsync(p => p.UtrNumber == request.UtrNumber && !p.IsDeleted, cancellationToken);
            if (utrExists)
            {
                throw new DomainException($"A payment with UTR number '{request.UtrNumber}' has already been processed.");
            }
        }

        var paymentNumber = await _numberGenerator.GeneratePaymentNumberAsync(cancellationToken);
        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = Money.FromDecimal(request.Amount),
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Paid,
            TransactionReference = request.TransactionReference,
            UtrNumber = request.UtrNumber,
            IdempotencyKey = request.IdempotencyKey,
            Notes = request.Notes,
            PaidAtUtc = DateTime.UtcNow
        };

        if (request.OrderId.HasValue)
        {
            var order = await _context.Orders
                .Include(o => o.Invoices)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId.Value, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Order), request.OrderId.Value);

            var remainingPaymentToApply = payment.Amount.AmountMinor;
            foreach (var inv in order.Invoices.OrderBy(i => i.IssuedAtUtc))
            {
                if (remainingPaymentToApply <= 0) break;

                var unpaidBalanceMinor = inv.GrandTotal.AmountMinor - inv.PaidAmount.AmountMinor;
                if (unpaidBalanceMinor <= 0) continue;

                var applyMinor = Math.Min(remainingPaymentToApply, unpaidBalanceMinor);
                inv.PaidAmount = new Money(inv.PaidAmount.AmountMinor + applyMinor, inv.PaidAmount.Currency);
                inv.BalanceAmount = new Money(inv.GrandTotal.AmountMinor - inv.PaidAmount.AmountMinor, inv.GrandTotal.Currency);
                inv.Status = inv.BalanceAmount.AmountMinor == 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
                remainingPaymentToApply -= applyMinor;
            }

            var allInvoicesPaid = order.Invoices.All(i => i.Status == InvoiceStatus.Paid);
            order.PaymentStatus = allInvoicesPaid ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);
        _context.Payments.Add(payment);

        await _auditLog.LogAsync(
            AuditAction.PaymentCreated,
            "Finance",
            nameof(Payment),
            payment.Id.ToString(),
            payment.PaymentNumber,
            after: new { payment.PaymentNumber, Amount = payment.Amount.ToDecimal(), payment.PaymentMethod, payment.UtrNumber },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("PaymentRecorded", new
        {
            PaymentId = payment.Id,
            payment.PaymentNumber,
            payment.OrderId,
            Amount = payment.Amount.ToDecimal(),
            payment.PaymentMethod
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var cust = await _context.Customers.FindAsync(new object[] { payment.CustomerId }, cancellationToken);

        return new PaymentDto
        {
            Id = payment.Id,
            PaymentNumber = payment.PaymentNumber,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            CustomerName = cust != null ? $"{cust.FirstName} {cust.LastName}".Trim() : "Customer",
            Amount = payment.Amount.ToDecimal(),
            PaymentMethod = payment.PaymentMethod,
            PaymentStatus = payment.PaymentStatus,
            TransactionReference = payment.TransactionReference,
            UtrNumber = payment.UtrNumber,
            IdempotencyKey = payment.IdempotencyKey,
            Notes = payment.Notes,
            PaidAtUtc = payment.PaidAtUtc
        };
    }

    public async Task<PagedResult<ExpenseDto>> GetExpensesAsync(int page = 1, int pageSize = 20, ExpenseCategory? category = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Expenses.AsNoTracking().Where(e => !e.IsDeleted);

        if (category.HasValue)
            query = query.Where(e => e.Category == category.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.ExpenseDateUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExpenseDto
            {
                Id = e.Id,
                ExpenseNumber = e.ExpenseNumber,
                Category = e.Category,
                Description = e.Description,
                Amount = e.Amount.ToDecimal(),
                Tax = e.Tax.ToDecimal(),
                PaymentMethod = e.PaymentMethod,
                ExpenseDateUtc = e.ExpenseDateUtc,
                Reference = e.Reference,
                CreatedBy = e.CreatedBy
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ExpenseDto>(items, totalCount, page, pageSize);
    }

    public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        var expenseNumber = await _numberGenerator.GenerateExpenseNumberAsync(cancellationToken);

        var expense = new Expense
        {
            ExpenseNumber = expenseNumber,
            Category = request.Category,
            Description = request.Description.Trim(),
            Amount = Money.FromDecimal(request.Amount),
            Tax = Money.FromDecimal(request.Tax),
            PaymentMethod = request.PaymentMethod,
            ExpenseDateUtc = request.ExpenseDateUtc ?? DateTime.UtcNow,
            Reference = request.Reference,
            CreatedBy = _currentUser.UserName ?? "Admin"
        };

        _context.Expenses.Add(expense);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Finance",
            nameof(Expense),
            expense.Id.ToString(),
            expense.ExpenseNumber,
            after: new { expense.ExpenseNumber, Amount = expense.Amount.ToDecimal(), Category = expense.Category.ToString() },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExpenseDto
        {
            Id = expense.Id,
            ExpenseNumber = expense.ExpenseNumber,
            Category = expense.Category,
            Description = expense.Description,
            Amount = expense.Amount.ToDecimal(),
            Tax = expense.Tax.ToDecimal(),
            PaymentMethod = expense.PaymentMethod,
            ExpenseDateUtc = expense.ExpenseDateUtc,
            Reference = expense.Reference,
            CreatedBy = expense.CreatedBy
        };
    }

    public async Task<ProfitLossDto> GetProfitLossAsync(DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default)
    {
        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && !o.IsDeleted);

        var expensesQuery = _context.Expenses.AsNoTracking().Where(e => !e.IsDeleted);

        if (fromDateUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.PlacedAtUtc >= fromDateUtc.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDateUtc >= fromDateUtc.Value);
        }

        if (toDateUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.PlacedAtUtc <= toDateUtc.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDateUtc <= toDateUtc.Value);
        }

        var orders = await ordersQuery.ToListAsync(cancellationToken);
        var expenses = await expensesQuery.ToListAsync(cancellationToken);

        decimal grossSales = orders.Sum(o => o.ItemsSubtotal.ToDecimal());
        decimal discounts = orders.Sum(o => o.Discount.ToDecimal());
        decimal returnsTotal = 0m;
        decimal cogs = orders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.CostPriceSnapshot.ToDecimal());
        decimal totalExpenses = expenses.Sum(e => e.Amount.ToDecimal());

        var transport = expenses.Where(e => e.Category == ExpenseCategory.Transport).Sum(e => e.Amount.ToDecimal());
        var packaging = expenses.Where(e => e.Category == ExpenseCategory.Packaging).Sum(e => e.Amount.ToDecimal());
        var rentAndUtilities = expenses.Where(e => e.Category == ExpenseCategory.Rent || e.Category == ExpenseCategory.Electricity || e.Category == ExpenseCategory.Warehouse).Sum(e => e.Amount.ToDecimal());
        var salaries = expenses.Where(e => e.Category == ExpenseCategory.Salary).Sum(e => e.Amount.ToDecimal());
        var marketing = expenses.Where(e => e.Category == ExpenseCategory.Marketing).Sum(e => e.Amount.ToDecimal());
        var officeAndAdmin = expenses.Where(e => e.Category == ExpenseCategory.Office || e.Category == ExpenseCategory.Other).Sum(e => e.Amount.ToDecimal());

        return new ProfitLossDto
        {
            GrossSales = grossSales,
            Discounts = discounts,
            ReturnsTotal = returnsTotal,
            CostOfGoodsSold = cogs,
            OperatingExpenses = totalExpenses,
            OperatingExpensesBreakdown = new ExpenseBreakdownDto
            {
                Transport = transport,
                Packaging = packaging,
                RentAndUtilities = rentAndUtilities,
                Salaries = salaries,
                Marketing = marketing,
                OfficeAndAdmin = officeAndAdmin,
                Total = totalExpenses
            }
        };
    }
}
