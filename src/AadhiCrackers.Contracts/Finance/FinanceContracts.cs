using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Finance;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Shipping { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime DueDateUtc { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? TransactionReference { get; set; }
    public string? UtrNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Notes { get; set; }
    public DateTime PaidAtUtc { get; set; }
}

public class CreatePaymentRequest
{
    public Guid? OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? TransactionReference { get; set; }
    public string? UtrNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Notes { get; set; }
}

public class ExpenseDto
{
    public Guid Id { get; set; }
    public string ExpenseNumber { get; set; } = string.Empty;
    public ExpenseCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Tax { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateTime ExpenseDateUtc { get; set; }
    public string? Reference { get; set; }
    public string? CreatedBy { get; set; }
}

public class CreateExpenseRequest
{
    public ExpenseCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Tax { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public DateTime? ExpenseDateUtc { get; set; }
    public string? Reference { get; set; }
}

public class SalesReportDto
{
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal AverageOrderValue => TotalOrders > 0 ? TotalSales / TotalOrders : 0;
    public List<SalesTrendPointDto> SalesTrend { get; set; } = new();
}

public class SalesTrendPointDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public int Orders { get; set; }
}

public class CategorySalesDto
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Percentage { get; set; }
}

public class TopProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}

public class PaymentMethodReportDto
{
    public string Method { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int Percentage { get; set; }
}

public class ExpenseBreakdownDto
{
    public decimal Transport { get; set; }
    public decimal Packaging { get; set; }
    public decimal RentAndUtilities { get; set; }
    public decimal Salaries { get; set; }
    public decimal Marketing { get; set; }
    public decimal OfficeAndAdmin { get; set; }
    public decimal Total { get; set; }
}

public class ProfitLossDto
{
    public decimal GrossSales { get; set; }
    public decimal TotalRevenue { get => GrossSales; set => GrossSales = value; }
    public decimal Discounts { get; set; }
    public decimal ReturnsTotal { get; set; }
    public decimal NetRevenue => Math.Max(0, GrossSales - Discounts - ReturnsTotal);
    public decimal NetSales => NetRevenue;
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit => NetRevenue - CostOfGoodsSold;
    public decimal GrossMarginPercentage => NetRevenue > 0 ? Math.Round((GrossProfit / NetRevenue) * 100, 2) : 0;
    public decimal OperatingExpenses { get; set; }
    public decimal TotalExpenses { get => OperatingExpenses; set => OperatingExpenses = value; }
    public ExpenseBreakdownDto OperatingExpensesBreakdown { get; set; } = new();
    public decimal NetProfit => GrossProfit - OperatingExpenses;
    public decimal NetOperatingProfit => NetProfit;
    public decimal ProfitMargin => NetRevenue > 0 ? Math.Round((NetProfit / NetRevenue) * 100, 2) : 0;
    public decimal NetMarginPercentage => ProfitMargin;
    public decimal NetProfitMarginPercentage => ProfitMargin;
}

public class DashboardKpiDto
{
    public decimal TotalSales { get; set; }
    public string SalesChangePercentage { get; set; } = "+18.5%";
    public int TotalOrders { get; set; }
    public string OrdersChangePercentage { get; set; } = "+12.4%";
    public int TotalCustomers { get; set; }
    public string CustomersChangePercentage { get; set; } = "+8.7%";
    public decimal TotalProfit { get; set; }
    public string ProfitChangePercentage { get; set; } = "+22.1%";
    public int LowStockItems { get; set; }
    public int PendingOrders { get; set; }
    public decimal StockValue { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int OutstandingInvoicesCount { get; set; }
    public decimal TodaySales { get; set; }
    public int TodayOrders { get; set; }
    public string BestSellingBrand { get; set; } = "AADHI CRACKERS";
    public int BestSellingBrandShare { get; set; } = 65;
}
