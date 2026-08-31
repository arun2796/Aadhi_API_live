using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Audit;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUser;

    public OrdersController(IOrderService orderService, ICurrentUserService currentUser)
    {
        _orderService = orderService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order == null)
            return NotFound(ApiResponse<OrderDto>.Fail($"Order with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<OrderDto>.Ok(order, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("customer/{customerId:guid}")]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetCustomerOrders(Guid customerId, CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetCustomerOrdersAsync(customerId, cancellationToken);
        return Ok(ApiResponse<List<OrderDto>>.Ok(orders, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.OrderCreate)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.CreateOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, ApiResponse<OrderDto>.Ok(order, "Order placed successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}/status")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.UpdateOrderStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Order status updated successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/verify-payment")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> VerifyPayment(Guid id, [FromBody] VerifyPaymentRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.VerifyPaymentAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Payment proof verified and order confirmed successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/move-to-packing")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> MoveToPacking(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.MoveToPackingAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Order moved to packing station", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/reject-payment")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> RejectPayment(Guid id, [FromBody] RejectPaymentRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.RejectPaymentAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Payment proof rejected and order cancelled", _currentUser.CorrelationId));
    }

    [HttpGet("track/{orderNumber}")]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<OrderTrackingDto>>> TrackOrder(string orderNumber, CancellationToken cancellationToken)
    {
        var tracking = await _orderService.TrackOrderAsync(orderNumber, cancellationToken);
        if (tracking == null)
            return NotFound(ApiResponse<OrderTrackingDto>.Fail($"No order found for '{orderNumber}'", _currentUser.CorrelationId));

        return Ok(ApiResponse<OrderTrackingDto>.Ok(tracking, correlationId: _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUser;

    public InventoryController(IInventoryService inventoryService, ICurrentUserService currentUser)
    {
        _inventoryService = inventoryService;
        _currentUser = currentUser;
    }

    [HttpGet("warehouses")]
    public async Task<ActionResult<ApiResponse<List<WarehouseDto>>>> GetWarehouses(CancellationToken cancellationToken)
    {
        var list = await _inventoryService.GetWarehousesAsync(cancellationToken);
        return Ok(ApiResponse<List<WarehouseDto>>.Ok(list, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("stock")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<StockItemDto>>>> GetStock(
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _inventoryService.GetStockItemsAsync(warehouseId, search, lowStockOnly, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<StockItemDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("adjustments")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<StockItemDto>>> AdjustStock([FromBody] StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var item = await _inventoryService.AdjustStockAsync(request, cancellationToken);
        return Ok(ApiResponse<StockItemDto>.Ok(item, "Stock adjusted successfully", _currentUser.CorrelationId));
    }

    [HttpPost("transfers")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> TransferStock([FromBody] StockTransferRequest request, CancellationToken cancellationToken)
    {
        var success = await _inventoryService.TransferStockAsync(request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Stock transferred successfully", _currentUser.CorrelationId));
    }

    [HttpGet("movements")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<StockMovementDto>>>> GetMovements(
        [FromQuery] Guid? productId = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _inventoryService.GetStockMovementsAsync(productId, warehouseId, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<StockMovementDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<ApiResponse<List<LowStockAlertDto>>>> GetLowStockAlerts([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var alerts = await _inventoryService.GetLowStockAlertsAsync(limit, cancellationToken);
        return Ok(ApiResponse<List<LowStockAlertDto>>.Ok(alerts, correlationId: _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchaseService;
    private readonly ICurrentUserService _currentUser;

    public PurchasesController(IPurchaseService purchaseService, ICurrentUserService currentUser)
    {
        _purchaseService = purchaseService;
        _currentUser = currentUser;
    }

    [HttpGet("suppliers")]
    public async Task<ActionResult<ApiResponse<List<SupplierDto>>>> GetSuppliers(CancellationToken cancellationToken)
    {
        var suppliers = await _purchaseService.GetSuppliersAsync(cancellationToken);
        return Ok(ApiResponse<List<SupplierDto>>.Ok(suppliers, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("suppliers")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> CreateSupplier([FromBody] CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        var supplier = await _purchaseService.CreateSupplierAsync(request, cancellationToken);
        return Ok(ApiResponse<SupplierDto>.Ok(supplier, "Supplier created successfully", _currentUser.CorrelationId));
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<PurchaseOrderDto>>>> GetPurchaseOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _purchaseService.GetPurchaseOrdersAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<PurchaseOrderDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> GetPurchaseOrderById(Guid id, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.GetPurchaseOrderByIdAsync(id, cancellationToken);
        if (po == null)
            return NotFound(ApiResponse<PurchaseOrderDto>.Fail($"PO with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> CreatePurchaseOrder([FromBody] CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.CreatePurchaseOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = po.Id }, ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order created", _currentUser.CorrelationId));
    }

    [HttpPost("goods-receipts")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<GoodsReceiptDto>>> CreateGoodsReceipt([FromBody] CreateGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var grn = await _purchaseService.CreateGoodsReceiptAsync(request, cancellationToken);
        return Ok(ApiResponse<GoodsReceiptDto>.Ok(grn, "Goods received and stock updated", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public InvoicesController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceDto>>>> GetInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetInvoicesAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<InvoiceDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public PaymentsController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentDto>>>> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetPaymentsAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _financeService.CreatePaymentAsync(request, cancellationToken);
        return Ok(ApiResponse<PaymentDto>.Ok(payment, "Payment recorded successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class ExpensesController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public ExpensesController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<ExpenseDto>>>> GetExpenses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ExpenseCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetExpensesAsync(page, pageSize, category, cancellationToken);
        return Ok(ApiResponse<PagedResult<ExpenseDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await _financeService.CreateExpenseAsync(request, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.Ok(expense, "Expense recorded successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public ReportsController(
        IReportService reportService,
        IFinanceService financeService,
        ICurrentUserService currentUser)
    {
        _reportService = reportService;
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet("dashboard-kpis")]
    public async Task<ActionResult<ApiResponse<DashboardKpiDto>>> GetDashboardKpis(CancellationToken cancellationToken)
    {
        var kpis = await _reportService.GetDashboardKpisAsync(cancellationToken);
        return Ok(ApiResponse<DashboardKpiDto>.Ok(kpis, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("sales-overview")]
    [EnableRateLimiting(RateLimitingPolicies.Reports)]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> GetSalesOverview([FromQuery] string period = "month", CancellationToken cancellationToken = default)
    {
        var report = await _reportService.GetSalesOverviewAsync(period, cancellationToken);
        return Ok(ApiResponse<SalesReportDto>.Ok(report, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("top-categories")]
    public async Task<ActionResult<ApiResponse<List<CategorySalesDto>>>> GetTopCategories(CancellationToken cancellationToken)
    {
        var categories = await _reportService.GetTopSellingCategoriesAsync(cancellationToken);
        return Ok(ApiResponse<List<CategorySalesDto>>.Ok(categories, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("top-products")]
    public async Task<ActionResult<ApiResponse<List<TopProductDto>>>> GetTopProducts([FromQuery] int limit = 5, CancellationToken cancellationToken = default)
    {
        var products = await _reportService.GetTopSellingProductsAsync(limit, cancellationToken);
        return Ok(ApiResponse<List<TopProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<ApiResponse<List<PaymentMethodReportDto>>>> GetPaymentMethods(CancellationToken cancellationToken)
    {
        var methods = await _reportService.GetSalesByPaymentMethodAsync(cancellationToken);
        return Ok(ApiResponse<List<PaymentMethodReportDto>>.Ok(methods, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("profit-loss")]
    [EnableRateLimiting(RateLimitingPolicies.Reports)]
    public async Task<ActionResult<ApiResponse<ProfitLossDto>>> GetProfitLoss(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var pnl = await _financeService.GetProfitLossAsync(fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<ProfitLossDto>.Ok(pnl, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("export/{reportType}")]
    [EnableRateLimiting(RateLimitingPolicies.Reports)]
    public async Task<IActionResult> ExportCsv(
        string reportType,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _reportService.GenerateCsvExportAsync(reportType, fromDate, toDate, cancellationToken);
        return File(bytes, "text/csv", $"aadhi_{reportType}_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;
    private readonly ICurrentUserService _currentUser;

    public AuditLogsController(IAuditLogService auditLogService, ICurrentUserService currentUser)
    {
        _auditLogService = auditLogService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AuditSearch)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetAuditLogs([FromQuery] AuditLogFilterRequest filter, CancellationToken cancellationToken)
    {
        var result = await _auditLogService.GetAuditLogsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.AuditSearch)]
    public async Task<ActionResult<ApiResponse<AuditLogDetailDto>>> GetAuditLogById(Guid id, CancellationToken cancellationToken)
    {
        var item = await _auditLogService.GetAuditLogByIdAsync(id, cancellationToken);
        if (item == null)
            return NotFound(ApiResponse<AuditLogDetailDto>.Fail($"Audit log with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<AuditLogDetailDto>.Ok(item, correlationId: _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;
    private readonly ICurrentUserService _currentUser;

    public SettingsController(ISettingsService settingsService, ICurrentUserService currentUser)
    {
        _settingsService = settingsService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Contracts.Audit.SystemSettingDto>>>> GetSettings([FromQuery] string? group = null, CancellationToken cancellationToken = default)
    {
        var list = await _settingsService.GetSettingsAsync(group, cancellationToken);
        return Ok(ApiResponse<List<Contracts.Audit.SystemSettingDto>>.Ok(list, correlationId: _currentUser.CorrelationId));
    }

    [HttpPut]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateSetting([FromBody] Contracts.Audit.UpdateSettingRequest request, CancellationToken cancellationToken)
    {
        var success = await _settingsService.UpdateSettingAsync(request.Key, request.Value, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Setting updated successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly ICurrentUserService _currentUser;

    public SearchController(ISearchService searchService, ICurrentUserService currentUser)
    {
        _searchService = searchService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.ProductSearch)]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> Search(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(new PagedResult<ProductDto>(), correlationId: _currentUser.CorrelationId));

        var result = await _searchService.SearchProductsAsync(q, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }
}
