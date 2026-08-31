using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Invoice : AggregateRoot<Guid>
{
    public string InvoiceNumber { get; set; } = string.Empty; // e.g. INV-2026-000001
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Money Subtotal { get; set; } = Money.Zero();
    public Money Discount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public Money Shipping { get; set; } = Money.Zero();
    public Money GrandTotal { get; set; } = Money.Zero();
    public Money PaidAmount { get; set; } = Money.Zero();
    public Money BalanceAmount { get; set; } = Money.Zero();

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.AddDays(7);

    public Invoice()
    {
        Id = Guid.NewGuid();
    }
}

public class Payment : AggregateRoot<Guid>
{
    public string PaymentNumber { get; set; } = string.Empty; // e.g. PAY-2026-000001
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Money Amount { get; set; } = Money.Zero();
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;

    public Payment()
    {
        Id = Guid.NewGuid();
    }
}

public class Expense : AggregateRoot<Guid>
{
    public string ExpenseNumber { get; set; } = string.Empty; // e.g. EXP-2026-000001
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public string Description { get; set; } = string.Empty;
    public Money Amount { get; set; } = Money.Zero();
    public Money Tax { get; set; } = Money.Zero();
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public DateTime ExpenseDateUtc { get; set; } = DateTime.UtcNow;
    public string? Reference { get; set; }

    public Expense()
    {
        Id = Guid.NewGuid();
    }
}
