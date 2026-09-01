using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Audit;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly ICurrentUserService _currentUser;

    public OrdersController(IOrderService orderService, ICustomerService customerService, ICurrentUserService currentUser)
    {
        _orderService = orderService;
        _customerService = customerService;
        _currentUser = currentUser;
    }

    [HttpGet("my-orders")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetMyOrdersAsync(cancellationToken);
        return Ok(ApiResponse<List<OrderDto>>.Ok(orders, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        // Enforce customer ownership check to prevent IDOR
        if (_currentUser.Role == "Customer")
        {
            var customer = !string.IsNullOrWhiteSpace(_currentUser.UserId)
                ? await _customerService.GetCustomerByUserIdAsync(_currentUser.UserId, cancellationToken)
                : (!string.IsNullOrWhiteSpace(_currentUser.Email) ? await _customerService.GetCustomerByEmailAsync(_currentUser.Email, cancellationToken) : null);

            if (customer == null)
            {
                return Ok(ApiResponse<PagedResult<OrderDto>>.Ok(new PagedResult<OrderDto>(new List<OrderDto>(), 0, page, pageSize), correlationId: _currentUser.CorrelationId));
            }

            var customerOrders = await _orderService.GetOrdersByCustomerIdAsync(customer.Id, page, pageSize, cancellationToken);
            return Ok(ApiResponse<PagedResult<OrderDto>>.Ok(customerOrders, correlationId: _currentUser.CorrelationId));
        }

        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order == null)
            return NotFound(ApiResponse<OrderDto>.Fail($"Order with ID '{id}' not found", _currentUser.CorrelationId));

        // Prevent IDOR: Customers can only view their own order
        if (_currentUser.Role == "Customer")
        {
            var customer = !string.IsNullOrWhiteSpace(_currentUser.UserId)
                ? await _customerService.GetCustomerByUserIdAsync(_currentUser.UserId, cancellationToken)
                : (!string.IsNullOrWhiteSpace(_currentUser.Email) ? await _customerService.GetCustomerByEmailAsync(_currentUser.Email, cancellationToken) : null);

            if (customer == null || order.CustomerId != customer.Id)
            {
                return Forbid();
            }
        }

        return Ok(ApiResponse<OrderDto>.Ok(order, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("customer/{customerId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetCustomerOrders(string customerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(customerId, out var cid))
        {
            return Ok(ApiResponse<List<OrderDto>>.Ok(new List<OrderDto>(), correlationId: _currentUser.CorrelationId));
        }

        if (_currentUser.Role == "Customer")
        {
            var customer = !string.IsNullOrWhiteSpace(_currentUser.UserId)
                ? await _customerService.GetCustomerByUserIdAsync(_currentUser.UserId, cancellationToken)
                : (!string.IsNullOrWhiteSpace(_currentUser.Email) ? await _customerService.GetCustomerByEmailAsync(_currentUser.Email, cancellationToken) : null);

            if (customer == null || customer.Id != cid)
            {
                return Forbid();
            }
        }

        var orders = await _orderService.GetCustomerOrdersAsync(cid, cancellationToken);
        return Ok(ApiResponse<List<OrderDto>>.Ok(orders, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.OrderCreate)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.CreateOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, ApiResponse<OrderDto>.Ok(order, "Order placed successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.UpdateOrderStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Order status updated successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/verify-payment")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> VerifyPayment(Guid id, [FromBody] VerifyPaymentRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.VerifyPaymentAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Payment verified and order confirmed successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/move-to-packing")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> MoveToPacking(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.MoveToPackingAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Order status moved to packing station", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/reject-payment")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> RejectPayment(Guid id, [FromBody] RejectPaymentRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.RejectPaymentAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Payment proof rejected and order cancelled", _currentUser.CorrelationId));
    }

    [HttpGet("track/{orderNumber}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<OrderTrackingDto>>> TrackOrder(string orderNumber, CancellationToken cancellationToken)
    {
        var tracking = await _orderService.TrackOrderAsync(orderNumber, cancellationToken);
        if (tracking == null)
            return NotFound(ApiResponse<OrderTrackingDto>.Fail($"Tracking information for '{orderNumber}' not found", _currentUser.CorrelationId));

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
    [Authorize(Policy = "RequireInventoryManager")]
    public async Task<ActionResult<ApiResponse<List<WarehouseDto>>>> GetWarehouses(CancellationToken cancellationToken)
    {
        var list = await _inventoryService.GetWarehousesAsync(cancellationToken);
        return Ok(ApiResponse<List<WarehouseDto>>.Ok(list, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("stock")]
    [HttpGet("stock-items")]
    [Authorize(Policy = "RequireInventoryManager")]
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
    [HttpPost("adjust")]
    [Authorize(Policy = "RequireInventoryManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<StockItemDto>>> AdjustStock([FromBody] StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var item = await _inventoryService.AdjustStockAsync(request, cancellationToken);
        return Ok(ApiResponse<StockItemDto>.Ok(item, "Stock adjusted successfully", _currentUser.CorrelationId));
    }

    [HttpPost("transfers")]
    [HttpPost("transfer")]
    [Authorize(Policy = "RequireInventoryManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> TransferStock([FromBody] StockTransferRequest request, CancellationToken cancellationToken)
    {
        var success = await _inventoryService.TransferStockAsync(request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Stock transferred successfully", _currentUser.CorrelationId));
    }

    [HttpGet("movements")]
    [Authorize(Policy = "RequireInventoryManager")]
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
    [Authorize(Policy = "RequireInventoryManager")]
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
    [Authorize(Policy = "RequirePurchaseManager")]
    public async Task<ActionResult<ApiResponse<List<SupplierDto>>>> GetSuppliers(CancellationToken cancellationToken)
    {
        var suppliers = await _purchaseService.GetSuppliersAsync(cancellationToken);
        return Ok(ApiResponse<List<SupplierDto>>.Ok(suppliers, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("suppliers")]
    [Authorize(Policy = "RequirePurchaseManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> CreateSupplier([FromBody] CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        var supplier = await _purchaseService.CreateSupplierAsync(request, cancellationToken);
        return Ok(ApiResponse<SupplierDto>.Ok(supplier, "Supplier created successfully", _currentUser.CorrelationId));
    }

    [HttpGet]
    [Authorize(Policy = "RequirePurchaseManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<PurchaseOrderDto>>>> GetPurchaseOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] PurchaseOrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _purchaseService.GetPurchaseOrdersAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<PurchaseOrderDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequirePurchaseManager")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> GetPurchaseOrderById(Guid id, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.GetPurchaseOrderByIdAsync(id, cancellationToken);
        if (po == null)
            return NotFound(ApiResponse<PurchaseOrderDto>.Fail($"PO with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequirePurchaseManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> CreatePurchaseOrder([FromBody] CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.CreatePurchaseOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = po.Id }, ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order created", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "RequirePurchaseManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> SubmitPurchaseOrder(Guid id, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.SubmitPurchaseOrderAsync(id, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order submitted for approval", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> ApprovePurchaseOrder(Guid id, [FromBody] ApprovePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.ApprovePurchaseOrderAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order approved", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> RejectPurchaseOrder(Guid id, [FromBody] RejectPurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.RejectPurchaseOrderAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order rejected", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "RequirePurchaseManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> CancelPurchaseOrder(Guid id, [FromBody] CancelPurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var po = await _purchaseService.CancelPurchaseOrderAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(po, "Purchase order cancelled", _currentUser.CorrelationId));
    }

    [HttpPost("goods-receipts")]
    [Authorize(Policy = "RequirePurchaseManager")]
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
    [Authorize(Policy = "RequireAccountant")]
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
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentDto>>>> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetPaymentsAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> GetPaymentById(Guid id, CancellationToken cancellationToken)
    {
        var payment = await _financeService.GetPaymentByIdAsync(id, cancellationToken);
        if (payment == null)
            return NotFound(ApiResponse<PaymentDto>.Fail($"Payment with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<PaymentDto>.Ok(payment, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _financeService.CreatePaymentAsync(request, cancellationToken);
        return Ok(ApiResponse<PaymentDto>.Ok(payment, "Payment recorded successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class ReturnsController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUser;

    public ReturnsController(IOrderService orderService, ICurrentUserService currentUser)
    {
        _orderService = orderService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireStaff")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<ReturnOrderDto>>>> GetReturns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetReturnOrdersAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<ReturnOrderDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ReturnOrderDto>>> GetReturnById(Guid id, CancellationToken cancellationToken)
    {
        var returnOrder = await _orderService.GetReturnOrderByIdAsync(id, cancellationToken);
        if (returnOrder == null)
            return NotFound(ApiResponse<ReturnOrderDto>.Fail($"Return order with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<ReturnOrderDto>.Ok(returnOrder, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.OrderCreate)]
    public async Task<ActionResult<ApiResponse<ReturnOrderDto>>> CreateReturnOrder([FromBody] CreateReturnOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.CreateReturnOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetReturnById), new { id = result.Id }, ApiResponse<ReturnOrderDto>.Ok(result, "Return requested successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "RequireStaff")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ReturnOrderDto>>> ApproveReturn(Guid id, [FromBody] string? notes, CancellationToken cancellationToken)
    {
        var result = await _orderService.ApproveReturnOrderAsync(id, notes, cancellationToken);
        return Ok(ApiResponse<ReturnOrderDto>.Ok(result, "Return request approved", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = "RequireInventoryManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ReturnOrderDto>>> ReceiveReturn(Guid id, [FromBody] string? notes, CancellationToken cancellationToken)
    {
        var result = await _orderService.ReceiveReturnOrderAsync(id, notes, cancellationToken);
        return Ok(ApiResponse<ReturnOrderDto>.Ok(result, "Return package marked as received", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/inspect")]
    [Authorize(Policy = "RequireInventoryManager")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ReturnOrderDto>>> InspectReturn(Guid id, [FromBody] InspectReturnOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.InspectReturnOrderAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ReturnOrderDto>.Ok(result, "Return inspection completed and sellable items restocked", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class RefundsController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public RefundsController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<RefundDto>>>> GetRefunds(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? orderId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetRefundsAsync(page, pageSize, orderId, cancellationToken);
        return Ok(ApiResponse<PagedResult<RefundDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<RefundDto>>> GetRefundById(Guid id, CancellationToken cancellationToken)
    {
        var refund = await _financeService.GetRefundByIdAsync(id, cancellationToken);
        if (refund == null)
            return NotFound(ApiResponse<RefundDto>.Fail($"Refund with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<RefundDto>.Ok(refund, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<RefundDto>>> CreateRefund([FromBody] CreateRefundRequest request, CancellationToken cancellationToken)
    {
        var refund = await _financeService.CreateRefundAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetRefundById), new { id = refund.Id }, ApiResponse<RefundDto>.Ok(refund, "Refund processed successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/supplier-bills")]
public class SupplierBillsController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public SupplierBillsController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<SupplierBillDto>>>> GetSupplierBills(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetSupplierBillsAsync(page, pageSize, supplierId, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<SupplierBillDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<SupplierBillDto>>> GetSupplierBillById(Guid id, CancellationToken cancellationToken)
    {
        var bill = await _financeService.GetSupplierBillByIdAsync(id, cancellationToken);
        if (bill == null)
            return NotFound(ApiResponse<SupplierBillDto>.Fail($"Supplier bill with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<SupplierBillDto>.Ok(bill, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<SupplierBillDto>>> CreateSupplierBill([FromBody] CreateSupplierBillRequest request, CancellationToken cancellationToken)
    {
        var bill = await _financeService.CreateSupplierBillAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetSupplierBillById), new { id = bill.Id }, ApiResponse<SupplierBillDto>.Ok(bill, "Supplier bill created successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/pay")]
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<SupplierBillDto>>> PaySupplierBill(Guid id, [FromBody] PaySupplierBillRequest request, CancellationToken cancellationToken)
    {
        var bill = await _financeService.PaySupplierBillAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SupplierBillDto>.Ok(bill, "Supplier bill payment recorded", _currentUser.CorrelationId));
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
    [Authorize(Policy = "RequireAccountant")]
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
    [Authorize(Policy = "RequireAccountant")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await _financeService.CreateExpenseAsync(request, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.Ok(expense, "Expense recorded successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/finance")]
public class FinanceController : ControllerBase
{
    private readonly IFinanceService _financeService;
    private readonly ICurrentUserService _currentUser;

    public FinanceController(IFinanceService financeService, ICurrentUserService currentUser)
    {
        _financeService = financeService;
        _currentUser = currentUser;
    }

    [HttpGet("invoices")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceDto>>>> GetInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetInvoicesAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<InvoiceDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("expenses")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<PagedResult<ExpenseDto>>>> GetExpenses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ExpenseCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetExpensesAsync(page, pageSize, category, cancellationToken);
        return Ok(ApiResponse<PagedResult<ExpenseDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("expenses")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await _financeService.CreateExpenseAsync(request, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.Ok(expense, "Expense recorded successfully", _currentUser.CorrelationId));
    }

    [HttpGet("payments")]
    [Authorize(Policy = "RequireAccountant")]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentDto>>>> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _financeService.GetPaymentsAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
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

    [HttpGet("dashboard")]
    [HttpGet("dashboard-kpis")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<DashboardKpiDto>>> GetDashboardKpis(CancellationToken cancellationToken)
    {
        var kpis = await _reportService.GetDashboardKpisAsync(cancellationToken);
        return Ok(ApiResponse<DashboardKpiDto>.Ok(kpis, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("sales-trend")]
    [HttpGet("sales-overview")]
    [Authorize(Policy = "RequireStaff")]
    [EnableRateLimiting(RateLimitingPolicies.Reports)]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> GetSalesOverview([FromQuery] string period = "month", CancellationToken cancellationToken = default)
    {
        var report = await _reportService.GetSalesOverviewAsync(period, cancellationToken);
        return Ok(ApiResponse<SalesReportDto>.Ok(report, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("category-breakdown")]
    [HttpGet("top-categories")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<List<CategorySalesDto>>>> GetTopCategories(CancellationToken cancellationToken)
    {
        var categories = await _reportService.GetTopSellingCategoriesAsync(cancellationToken);
        return Ok(ApiResponse<List<CategorySalesDto>>.Ok(categories, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("top-products")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<List<TopProductDto>>>> GetTopProducts([FromQuery] int limit = 5, CancellationToken cancellationToken = default)
    {
        var products = await _reportService.GetTopSellingProductsAsync(limit, cancellationToken);
        return Ok(ApiResponse<List<TopProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("payment-methods")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<List<PaymentMethodReportDto>>>> GetPaymentMethods(CancellationToken cancellationToken)
    {
        var methods = await _reportService.GetSalesByPaymentMethodAsync(cancellationToken);
        return Ok(ApiResponse<List<PaymentMethodReportDto>>.Ok(methods, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("profit-loss")]
    [Authorize(Policy = "RequireAccountant")]
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
    [Authorize(Policy = "RequireAdmin")]
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
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AuditSearch)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetAuditLogs([FromQuery] AuditLogFilterRequest filter, CancellationToken cancellationToken)
    {
        var result = await _auditLogService.GetAuditLogsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
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
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<ApiResponse<List<SystemSettingDto>>>> GetSettings([FromQuery] string? group = null, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(group, cancellationToken);
        return Ok(ApiResponse<List<SystemSettingDto>>.Ok(settings, correlationId: _currentUser.CorrelationId));
    }

    [HttpPut("{key}")]
    [Authorize(Policy = "RequireSuperAdmin")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateSetting(string key, [FromBody] string value, CancellationToken cancellationToken)
    {
        var success = await _settingsService.UpdateSettingAsync(key, value, cancellationToken);
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
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> Search([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var results = await _searchService.SearchProductsAsync(q, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(results, correlationId: _currentUser.CorrelationId));
    }
}
