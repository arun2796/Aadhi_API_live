using System.Globalization;
using System.Text;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IReportService
{
    Task<DashboardKpiDto> GetDashboardKpisAsync(CancellationToken cancellationToken = default);
    Task<SalesReportDto> GetSalesOverviewAsync(string period = "month", CancellationToken cancellationToken = default);
    Task<List<CategorySalesDto>> GetTopSellingCategoriesAsync(CancellationToken cancellationToken = default);
    Task<List<TopProductDto>> GetTopSellingProductsAsync(int limit = 5, CancellationToken cancellationToken = default);
    Task<List<PaymentMethodReportDto>> GetSalesByPaymentMethodAsync(CancellationToken cancellationToken = default);
    Task<byte[]> GenerateCsvExportAsync(string reportType, DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default);
}

public class ReportService : IReportService
{
    private readonly IApplicationDbContext _context;

    public ReportService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardKpiDto> GetDashboardKpisAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var thirtyDaysAgo = now.AddDays(-30);
        var sixtyDaysAgo = now.AddDays(-60);

        // Fetch valid non-cancelled orders
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted && o.OrderStatus != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var todayOrders = orders.Where(o => o.PlacedAtUtc.Date == today).ToList();
        var currentPeriodOrders = orders.Where(o => o.PlacedAtUtc >= thirtyDaysAgo).ToList();
        var priorPeriodOrders = orders.Where(o => o.PlacedAtUtc >= sixtyDaysAgo && o.PlacedAtUtc < thirtyDaysAgo).ToList();

        var totalSales = orders.Sum(o => o.GrandTotal.ToDecimal());
        var todaySales = todayOrders.Sum(o => o.GrandTotal.ToDecimal());
        var currentSales = currentPeriodOrders.Sum(o => o.GrandTotal.ToDecimal());
        var priorSales = priorPeriodOrders.Sum(o => o.GrandTotal.ToDecimal());

        var salesPctChange = priorSales > 0 ? ((currentSales - priorSales) / priorSales) * 100 : (currentSales > 0 ? 100 : 0);
        var ordersPctChange = priorPeriodOrders.Count > 0 ? ((decimal)(currentPeriodOrders.Count - priorPeriodOrders.Count) / priorPeriodOrders.Count) * 100 : (currentPeriodOrders.Count > 0 ? 100 : 0);

        var customers = await _context.Customers.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync(cancellationToken);
        var totalCustomers = customers.Count;
        var currentCustCount = customers.Count(c => c.CreatedAtUtc >= thirtyDaysAgo);
        var priorCustCount = customers.Count(c => c.CreatedAtUtc >= sixtyDaysAgo && c.CreatedAtUtc < thirtyDaysAgo);
        var custPctChange = priorCustCount > 0 ? ((decimal)(currentCustCount - priorCustCount) / priorCustCount) * 100 : (currentCustCount > 0 ? 100 : 0);

        var pendingOrders = orders.Count(o => o.OrderStatus == OrderStatus.Pending);

        var products = await _context.Products.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync(cancellationToken);
        var lowStockCount = products.Count(p => (p.StockQuantity - p.ReservedQuantity) <= p.ReorderLevel);
        var totalStockValue = products.Sum(p => p.StockQuantity * p.CostPrice.ToDecimal());

        var invoices = await _context.Invoices.AsNoTracking().Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled).ToListAsync(cancellationToken);
        var outstandingAmount = invoices.Sum(i => i.BalanceAmount.ToDecimal());

        var expenses = await _context.Expenses.AsNoTracking().Where(e => !e.IsDeleted).ToListAsync(cancellationToken);
        var totalExpenses = expenses.Sum(e => e.Amount.ToDecimal());

        // Real COGS calculation from order items snapshots
        var totalCogs = orders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.CostPriceSnapshot.ToDecimal());
        var totalProfit = totalSales - totalCogs - totalExpenses;

        var currentExpenses = expenses.Where(e => e.ExpenseDateUtc >= thirtyDaysAgo).Sum(e => e.Amount.ToDecimal());
        var currentCogs = currentPeriodOrders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.CostPriceSnapshot.ToDecimal());
        var currentProfit = currentSales - currentCogs - currentExpenses;

        var priorExpenses = expenses.Where(e => e.ExpenseDateUtc >= sixtyDaysAgo && e.ExpenseDateUtc < thirtyDaysAgo).Sum(e => e.Amount.ToDecimal());
        var priorCogs = priorPeriodOrders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.CostPriceSnapshot.ToDecimal());
        var priorProfit = priorSales - priorCogs - priorExpenses;
        var profitPctChange = priorProfit != 0 ? ((currentProfit - priorProfit) / Math.Abs(priorProfit)) * 100 : (currentProfit > 0 ? 100 : 0);

        // Compute Brand share dynamically
        var brandGroups = await _context.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Brand != null)
            .GroupBy(p => p.Brand!.Name)
            .Select(g => new { BrandName = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(cancellationToken);

        var topBrandName = brandGroups?.BrandName ?? "AADHI CRACKERS";
        var topBrandShare = products.Count > 0 && brandGroups != null
            ? (int)Math.Round(((double)brandGroups.Count / products.Count) * 100)
            : 100;

        return new DashboardKpiDto
        {
            TotalSales = totalSales,
            SalesChangePercentage = $"{(salesPctChange >= 0 ? "+" : "")}{salesPctChange:F1}%",
            TotalOrders = orders.Count,
            OrdersChangePercentage = $"{(ordersPctChange >= 0 ? "+" : "")}{ordersPctChange:F1}%",
            TotalCustomers = totalCustomers,
            CustomersChangePercentage = $"{(custPctChange >= 0 ? "+" : "")}{custPctChange:F1}%",
            TotalProfit = totalProfit,
            ProfitChangePercentage = $"{(profitPctChange >= 0 ? "+" : "")}{profitPctChange:F1}%",
            LowStockItems = lowStockCount,
            PendingOrders = pendingOrders,
            StockValue = totalStockValue,
            OutstandingAmount = outstandingAmount,
            OutstandingInvoicesCount = invoices.Count,
            TodaySales = todaySales,
            TodayOrders = todayOrders.Count,
            BestSellingBrand = topBrandName,
            BestSellingBrandShare = topBrandShare
        };
    }

    public async Task<SalesReportDto> GetSalesOverviewAsync(string period = "month", CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var startDate = period.ToLower() switch
        {
            "7days" => now.AddDays(-7),
            "month" => now.AddDays(-30),
            "quarter" => now.AddDays(-90),
            "year" => now.AddDays(-365),
            _ => now.AddDays(-30)
        };

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.PlacedAtUtc >= startDate && o.OrderStatus != OrderStatus.Cancelled && !o.IsDeleted)
            .OrderBy(o => o.PlacedAtUtc)
            .ToListAsync(cancellationToken);

        var trend = new List<SalesTrendPointDto>();

        var grouped = orders.GroupBy(o => o.PlacedAtUtc.ToString("dd MMM", CultureInfo.InvariantCulture));
        foreach (var g in grouped)
        {
            trend.Add(new SalesTrendPointDto
            {
                Date = g.Key,
                Sales = g.Sum(o => o.GrandTotal.ToDecimal()),
                Orders = g.Count()
            });
        }

        var totalSales = orders.Sum(o => o.GrandTotal.ToDecimal());
        var totalDiscount = orders.Sum(o => o.Discount.ToDecimal());
        var totalTax = orders.Sum(o => o.Tax.ToDecimal());

        return new SalesReportDto
        {
            TotalSales = totalSales,
            TotalOrders = orders.Count,
            TotalDiscount = totalDiscount,
            TotalTax = totalTax,
            SalesTrend = trend
        };
    }

    public async Task<List<CategorySalesDto>> GetTopSellingCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var items = await _context.OrderItems
            .AsNoTracking()
            .Include(i => i.Product)
            .ThenInclude(p => p!.Category)
            .Where(i => !i.IsDeleted && i.Order.OrderStatus != OrderStatus.Cancelled && !i.Order.IsDeleted)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new List<CategorySalesDto>();
        }

        var grouped = items
            .GroupBy(i => i.Product?.Category?.Name ?? "Uncategorized")
            .Select(g => new
            {
                CategoryName = g.Key,
                Revenue = g.Sum(x => x.LineTotal.ToDecimal())
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var grandTotal = grouped.Sum(x => x.Revenue);
        if (grandTotal == 0) grandTotal = 1;

        return grouped.Select(g => new CategorySalesDto
        {
            CategoryName = g.CategoryName,
            Revenue = g.Revenue,
            Percentage = (int)Math.Round((g.Revenue / grandTotal) * 100)
        }).ToList();
    }

    public async Task<List<TopProductDto>> GetTopSellingProductsAsync(int limit = 5, CancellationToken cancellationToken = default)
    {
        var items = await _context.OrderItems
            .AsNoTracking()
            .Include(i => i.Product)
            .ThenInclude(p => p!.Images)
            .Where(i => !i.IsDeleted && i.Order.OrderStatus != OrderStatus.Cancelled && !i.Order.IsDeleted)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new List<TopProductDto>();
        }

        var top = items
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                var sample = g.First();
                var prod = sample.Product;
                var primaryImg = prod?.Images.OrderBy(img => img.SortOrder).FirstOrDefault(img => img.IsPrimary)?.Url
                    ?? prod?.Images.OrderBy(img => img.SortOrder).FirstOrDefault()?.Url
                    ?? sample.ProductImageUrlSnapshot;

                return new TopProductDto
                {
                    ProductId = g.Key,
                    ProductName = sample.ProductNameSnapshot,
                    ImageUrl = primaryImg,
                    UnitsSold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.LineTotal.ToDecimal())
                };
            })
            .OrderByDescending(x => x.Revenue)
            .Take(limit)
            .ToList();

        return top;
    }

    public async Task<List<PaymentMethodReportDto>> GetSalesByPaymentMethodAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => !o.IsDeleted && o.OrderStatus != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return new List<PaymentMethodReportDto>();
        }

        var grouped = orders
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new
            {
                Method = g.Key.ToString(),
                TotalAmount = g.Sum(o => o.GrandTotal.ToDecimal())
            })
            .OrderByDescending(g => g.TotalAmount)
            .ToList();

        var total = grouped.Sum(x => x.TotalAmount);
        if (total == 0) total = 1;

        return grouped.Select(g => new PaymentMethodReportDto
        {
            Method = g.Method switch
            {
                "UPI" => "UPI",
                "Card" => "Credit/Debit Card",
                "COD" => "Cash on Delivery",
                "BankTransfer" => "Net Banking",
                _ => g.Method
            },
            TotalAmount = g.TotalAmount,
            Percentage = (int)Math.Round((g.TotalAmount / total) * 100)
        }).ToList();
    }

    public async Task<byte[]> GenerateCsvExportAsync(string reportType, DateTime? fromDateUtc = null, DateTime? toDateUtc = null, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        switch (reportType.ToLower())
        {
            case "products":
                sb.AppendLine("ID,SKU,Name,Category,Price,CostPrice,StockQuantity,ReorderLevel,IsActive");
                var prods = await _context.Products.Include(p => p.Category).AsNoTracking().Where(p => !p.IsDeleted).ToListAsync(cancellationToken);
                foreach (var p in prods)
                {
                    sb.AppendLine($"\"{p.Id}\",\"{p.SKU}\",\"{p.Name.Replace("\"", "\"\"")}\",\"{p.Category?.Name}\",{p.Price.ToDecimal()},{p.CostPrice.ToDecimal()},{p.StockQuantity},{p.ReorderLevel},{p.IsActive}");
                }
                break;

            case "orders":
                sb.AppendLine("OrderNumber,Customer,Status,PaymentMethod,PaymentStatus,Subtotal,Discount,Tax,Shipping,GrandTotal,PlacedAtUtc");
                var ords = await _context.Orders.Include(o => o.Customer).AsNoTracking().Where(o => !o.IsDeleted).ToListAsync(cancellationToken);
                foreach (var o in ords)
                {
                    sb.AppendLine($"\"{o.OrderNumber}\",\"{o.Customer?.FirstName} {o.Customer?.LastName}\",\"{o.OrderStatus}\",\"{o.PaymentMethod}\",\"{o.PaymentStatus}\",{o.ItemsSubtotal.ToDecimal()},{o.Discount.ToDecimal()},{o.Tax.ToDecimal()},{o.ShippingCharge.ToDecimal()},{o.GrandTotal.ToDecimal()},\"{o.PlacedAtUtc:yyyy-MM-dd HH:mm:ss}\"");
                }
                break;

            case "inventory":
                sb.AppendLine("SKU,Product,Warehouse,QuantityOnHand,QuantityReserved,QuantityAvailable,ReorderLevel,Status");
                var stocks = await _context.StockItems.Include(s => s.Product).Include(s => s.Warehouse).AsNoTracking().ToListAsync(cancellationToken);
                foreach (var s in stocks)
                {
                    var status = s.QuantityAvailable <= s.ReorderLevel ? "Low Stock" : "In Stock";
                    sb.AppendLine($"\"{s.Product.SKU}\",\"{s.Product.Name.Replace("\"", "\"\"")}\",\"{s.Warehouse.Name}\",{s.QuantityOnHand},{s.QuantityReserved},{s.QuantityAvailable},{s.ReorderLevel},\"{status}\"");
                }
                break;

            default:
                sb.AppendLine("Report,GeneratedAtUtc");
                sb.AppendLine($"\"{reportType}\",\"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\"");
                break;
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
