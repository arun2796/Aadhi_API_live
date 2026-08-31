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
    Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<ExpenseDto>> GetExpensesAsync(int page = 1, int pageSize = 20, ExpenseCategory? category = null, CancellationToken cancellationToken = default);
    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ProfitLossDto> GetProfitLossAsync(DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default);
}

public class FinanceService : IFinanceService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;

    public FinanceService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
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
                Notes = p.Notes,
                PaidAtUtc = p.PaidAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>(items, totalCount, page, pageSize);
    }

    public async Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var count = await _context.Payments.CountAsync(cancellationToken) + 1;
        var paymentNumber = $"PAY-{DateTime.UtcNow:yyyy}-{count:D6}";

        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = Money.FromDecimal(request.Amount),
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Paid,
            TransactionReference = request.TransactionReference,
            Notes = request.Notes,
            PaidAtUtc = DateTime.UtcNow
        };

        if (request.OrderId.HasValue)
        {
            var order = await _context.Orders.Include(o => o.Invoices).FirstOrDefaultAsync(o => o.Id == request.OrderId.Value, cancellationToken);
            if (order != null)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                foreach (var inv in order.Invoices)
                {
                    inv.PaidAmount += payment.Amount;
                    inv.BalanceAmount = Math.Max(0, inv.GrandTotal.ToDecimal() - inv.PaidAmount.ToDecimal()) > 0
                        ? Money.FromDecimal(inv.GrandTotal.ToDecimal() - inv.PaidAmount.ToDecimal())
                        : Money.Zero();
                    inv.Status = inv.BalanceAmount.AmountMinor == 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
                }
            }
        }

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PaymentCreated,
            "Finance",
            nameof(Payment),
            payment.Id.ToString(),
            payment.PaymentNumber,
            after: new { payment.PaymentNumber, Amount = payment.Amount.ToDecimal(), payment.PaymentMethod },
            cancellationToken: cancellationToken);

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
        var count = await _context.Expenses.CountAsync(cancellationToken) + 1;
        var expenseNumber = $"EXP-{DateTime.UtcNow:yyyy}-{count:D6}";

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
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Finance",
            nameof(Expense),
            expense.Id.ToString(),
            expense.ExpenseNumber,
            after: new { expense.ExpenseNumber, Amount = expense.Amount.ToDecimal(), Category = expense.Category.ToString() },
            cancellationToken: cancellationToken);

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

        decimal totalRevenue = orders.Sum(o => o.GrandTotal.ToDecimal());
        decimal cogs = orders.SelectMany(o => o.Items).Sum(i => i.Quantity * (i.UnitPrice.ToDecimal() * 0.65m)); // Approximate 65% cost price
        decimal totalExpenses = expenses.Sum(e => e.Amount.ToDecimal());

        return new ProfitLossDto
        {
            TotalRevenue = totalRevenue,
            CostOfGoodsSold = cogs,
            TotalExpenses = totalExpenses
        };
    }
}
