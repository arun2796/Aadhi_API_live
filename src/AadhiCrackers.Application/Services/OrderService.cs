using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> VerifyPaymentAsync(Guid orderId, VerifyPaymentRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> MoveToPackingAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<OrderDto> RejectPaymentAsync(Guid orderId, RejectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetOrdersAsync(int page = 1, int pageSize = 20, OrderStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetOrdersByCustomerIdAsync(Guid customerId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<OrderTrackingDto?> TrackOrderAsync(string orderNumberOrPhone, CancellationToken cancellationToken = default);
    Task<OrderDto> SubmitPaymentProofAsync(Guid orderId, SubmitPaymentProofRequest request, CancellationToken cancellationToken = default);

    // Return Order Workflow (Phase 7)
    Task<ReturnOrderDto> CreateReturnOrderAsync(CreateReturnOrderRequest request, CancellationToken cancellationToken = default);
    Task<ReturnOrderDto> ApproveReturnOrderAsync(Guid returnId, string? notes = null, CancellationToken cancellationToken = default);
    Task<ReturnOrderDto> ReceiveReturnOrderAsync(Guid returnId, string? notes = null, CancellationToken cancellationToken = default);
    Task<ReturnOrderDto> InspectReturnOrderAsync(Guid returnId, InspectReturnOrderRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<ReturnOrderDto>> GetReturnOrdersAsync(int page = 1, int pageSize = 20, string? status = null, CancellationToken cancellationToken = default);
    Task<ReturnOrderDto?> GetReturnOrderByIdAsync(Guid returnId, CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IOutboxService _outbox;
    private readonly IBusinessNumberGenerator _numberGenerator;

    public OrderService(
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

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            throw new DomainException("Cannot create an order with zero items.");

        // Find or create customer
        var customerEmail = _currentUser.Email ?? "guest@aadhicrackers.com";
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Email.ToLower() == customerEmail.ToLower() && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            customer = new Customer
            {
                UserId = _currentUser.UserId ?? Guid.NewGuid().ToString(),
                CustomerCode = $"CUST-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}",
                FirstName = string.IsNullOrWhiteSpace(request.ShippingAddress.FullName) ? "Guest" : request.ShippingAddress.FullName.Split(' ')[0],
                LastName = request.ShippingAddress.FullName.Contains(' ') ? request.ShippingAddress.FullName[(request.ShippingAddress.FullName.IndexOf(' ') + 1)..] : "User",
                Email = customerEmail,
                Phone = request.ShippingAddress.Phone,
                IsActive = true
            };
            _context.Customers.Add(customer);
        }

        // Get active warehouse (or primary warehouse)
        var warehouse = request.WarehouseId.HasValue
            ? await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId.Value && w.IsActive, cancellationToken)
            : await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && w.IsActive, cancellationToken)
              ?? await _context.Warehouses.FirstOrDefaultAsync(w => w.IsActive, cancellationToken);

        if (warehouse == null)
        {
            throw new DomainException("No active warehouse configured for order fulfillment.");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Images)
            .Include(p => p.BundleComponents)
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var orderNumber = await _numberGenerator.GenerateOrderNumberAsync(cancellationToken);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            WarehouseId = warehouse.Id,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            CouponCode = request.CouponCode,
            Notes = request.Notes,
            PlacedAtUtc = DateTime.UtcNow,
            UtrNumber = request.UtrNumber,
            PaymentScreenshotUrl = request.PaymentScreenshotUrl ?? request.PaymentScreenshotBase64,
            PaymentSubmittedAtUtc = !string.IsNullOrWhiteSpace(request.UtrNumber) || !string.IsNullOrWhiteSpace(request.PaymentScreenshotUrl) || !string.IsNullOrWhiteSpace(request.PaymentScreenshotBase64) ? DateTime.UtcNow : null,
            ShippingAddress = request.ShippingAddress,
            BillingAddress = request.BillingAddress ?? (request.ShippingAddress with { }),
            TrackingNumber = $"TRK-{Random.Shared.Next(10000000, 99999999)}"
        };

        var itemsSubtotal = Money.Zero();
        var totalTax = Money.Zero();

        foreach (var reqItem in request.Items)
        {
            if (!products.TryGetValue(reqItem.ProductId, out var product))
                throw new ResourceNotFoundException(nameof(Product), reqItem.ProductId);

            var stockItem = await _context.StockItems
                .FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == warehouse.Id, cancellationToken);

            if (stockItem == null || stockItem.QuantityAvailable < reqItem.Quantity)
            {
                throw new InsufficientStockException(product.SKU, stockItem?.QuantityAvailable ?? 0, reqItem.Quantity);
            }

            // Reserve stock in StockItem (single source of truth)
            stockItem.QuantityReserved += reqItem.Quantity;

            // Sync projection on Product
            product.StockQuantity = stockItem.QuantityOnHand;
            product.ReservedQuantity = stockItem.QuantityReserved;

            // If product is a Bundle with components, reserve components stock too
            if (product.ProductType == ProductType.Bundle && product.BundleComponents.Count > 0)
            {
                foreach (var comp in product.BundleComponents)
                {
                    var compReqQty = reqItem.Quantity * comp.Quantity;
                    var compStockItem = await _context.StockItems
                        .FirstOrDefaultAsync(s => s.ProductId == comp.ComponentProductId && s.WarehouseId == warehouse.Id, cancellationToken);

                    if (compStockItem == null || compStockItem.QuantityAvailable < compReqQty)
                    {
                        var compProduct = await _context.Products.FindAsync(new object[] { comp.ComponentProductId }, cancellationToken);
                        throw new InsufficientStockException(compProduct?.SKU ?? "BUNDLE_COMPONENT", compStockItem?.QuantityAvailable ?? 0, compReqQty);
                    }

                    compStockItem.QuantityReserved += compReqQty;
                    var compProd = await _context.Products.FindAsync(new object[] { comp.ComponentProductId }, cancellationToken);
                    if (compProd != null)
                    {
                        compProd.ReservedQuantity = compStockItem.QuantityReserved;
                    }

                    _context.StockMovements.Add(new StockMovement
                    {
                        ProductId = comp.ComponentProductId,
                        WarehouseId = warehouse.Id,
                        MovementType = StockMovementType.StockReserved,
                        QuantityChange = compReqQty,
                        QuantityBefore = compStockItem.QuantityOnHand,
                        QuantityAfter = compStockItem.QuantityOnHand,
                        ReferenceType = "BundleReservation",
                        ReferenceId = order.OrderNumber,
                        Reason = $"Component stock reserved for bundle {product.SKU} in Order #{order.OrderNumber}",
                        CreatedBy = _currentUser.UserName ?? "Customer",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            var unitPrice = product.Price;
            var lineTotalBeforeTax = unitPrice * reqItem.Quantity;
            var itemTax = lineTotalBeforeTax * (product.TaxRate / 100m);

            var primaryImg = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
                ?? product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                SKUSnapshot = product.SKU,
                ProductImageUrlSnapshot = primaryImg,
                UnitPrice = unitPrice,
                CostPriceSnapshot = product.CostPrice,
                Quantity = reqItem.Quantity,
                Discount = Money.Zero(),
                Tax = itemTax,
                LineTotal = lineTotalBeforeTax
            };

            order.Items.Add(orderItem);
            itemsSubtotal += lineTotalBeforeTax;
            totalTax += itemTax;

            // Generate stock movement reservation
            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                MovementType = StockMovementType.StockReserved,
                QuantityChange = reqItem.Quantity,
                QuantityBefore = stockItem.QuantityOnHand,
                QuantityAfter = stockItem.QuantityOnHand,
                ReferenceType = "OrderReservation",
                ReferenceId = order.OrderNumber,
                Reason = $"Stock reserved for Order #{order.OrderNumber}",
                CreatedBy = _currentUser.UserName ?? "Customer",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        order.ItemsSubtotal = itemsSubtotal;
        order.Tax = totalTax;

        // Apply Promotion/Coupon if specified
        var discount = Money.Zero();
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code.ToUpper() == request.CouponCode.Trim().ToUpper() && !p.IsDeleted, cancellationToken);
            if (promo != null)
            {
                var customerUsageCount = await _context.PromotionRedemptions
                    .CountAsync(r => r.PromotionId == promo.Id && r.CustomerId == customer.Id && !r.IsDeleted, cancellationToken);

                if (promo.IsValidForOrder(itemsSubtotal, customerUsageCount))
                {
                    discount = promo.CalculateDiscount(itemsSubtotal);
                    promo.UsedCount++;
                    promo.RowVersion = Guid.NewGuid();
                    order.CouponCode = promo.Code;

                    _context.PromotionRedemptions.Add(new PromotionRedemption
                    {
                        PromotionId = promo.Id,
                        CustomerId = customer.Id,
                        OrderId = order.Id,
                        RedeemedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }
        order.Discount = discount;

        // Shipping calculation: Free above ₹3000, else ₹150
        var shippingCharge = itemsSubtotal >= Money.FromDecimal(3000m) ? Money.Zero() : Money.FromDecimal(150m);
        order.ShippingCharge = shippingCharge;

        order.GrandTotal = itemsSubtotal - discount + totalTax + shippingCharge;

        // Initial Order History
        order.StatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Pending,
            Reason = "Order Placed Successfully",
            ChangedBy = _currentUser.UserName ?? "Customer",
            ChangedAtUtc = DateTime.UtcNow
        });

        // Automatically create Invoice
        var invoiceNumber = await _numberGenerator.GenerateInvoiceNumberAsync(cancellationToken);
        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            OrderId = order.Id,
            CustomerId = customer.Id,
            Subtotal = order.ItemsSubtotal,
            Discount = order.Discount,
            Tax = order.Tax,
            Shipping = order.ShippingCharge,
            GrandTotal = order.GrandTotal,
            PaidAmount = order.PaymentStatus == PaymentStatus.Paid ? order.GrandTotal : Money.Zero(),
            BalanceAmount = order.PaymentStatus == PaymentStatus.Paid ? Money.Zero() : order.GrandTotal,
            Status = order.PaymentStatus == PaymentStatus.Paid ? InvoiceStatus.Paid : InvoiceStatus.Issued,
            IssuedAtUtc = DateTime.UtcNow,
            DueDateUtc = DateTime.UtcNow.AddDays(7)
        };
        order.Invoices.Add(invoice);

        _context.Orders.Add(order);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        // Enterprise Audit Logging
        await _auditLog.LogAsync(
            AuditAction.OrderCreated,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            after: new { order.OrderNumber, GrandTotal = order.GrandTotal.ToDecimal(), order.OrderStatus },
            cancellationToken: cancellationToken);

        // Outbox event for background email & elastic indexing
        await _outbox.EnqueueAsync("OrderPlaced", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            CustomerId = customer.Id,
            CustomerEmail = customer.Email,
            CustomerPhone = customer.Phone,
            GrandTotal = order.GrandTotal.ToDecimal()
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created order");
    }

    public async Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        var oldStatus = order.OrderStatus;
        order.ChangeStatus(request.NewStatus, request.Reason, _currentUser.UserName ?? "Admin");

        var warehouseId = order.WarehouseId;
        if (!warehouseId.HasValue)
        {
            var primaryWh = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && w.IsActive, cancellationToken)
                ?? await _context.Warehouses.FirstOrDefaultAsync(w => w.IsActive, cancellationToken);
            warehouseId = primaryWh?.Id;
        }

        // Handle Cancellation -> Release reserved stock
        if (request.NewStatus == OrderStatus.Cancelled)
        {
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var products = await _context.Products
                .Include(p => p.BundleComponents)
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);

                if (warehouseId.HasValue)
                {
                    var stockItem = await _context.StockItems
                        .FirstOrDefaultAsync(s => s.ProductId == item.ProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                    if (stockItem != null)
                    {
                        stockItem.QuantityReserved = Math.Max(0, stockItem.QuantityReserved - item.Quantity);
                        if (prod != null)
                        {
                            prod.StockQuantity = stockItem.QuantityOnHand;
                            prod.ReservedQuantity = stockItem.QuantityReserved;
                        }

                        _context.StockMovements.Add(new StockMovement
                        {
                            ProductId = item.ProductId,
                            WarehouseId = warehouseId.Value,
                            MovementType = StockMovementType.StockReservationReleased,
                            QuantityChange = item.Quantity,
                            QuantityBefore = stockItem.QuantityOnHand,
                            QuantityAfter = stockItem.QuantityOnHand,
                            ReferenceType = "OrderCancellation",
                            ReferenceId = order.OrderNumber,
                            Reason = $"Released reserved stock due to Order #{order.OrderNumber} cancellation",
                            CreatedBy = _currentUser.UserName ?? "Admin",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }

                    // Release components if bundle
                    if (prod != null && prod.ProductType == ProductType.Bundle && prod.BundleComponents.Count > 0)
                    {
                        foreach (var comp in prod.BundleComponents)
                        {
                            var compReleaseQty = item.Quantity * comp.Quantity;
                            var compStockItem = await _context.StockItems
                                .FirstOrDefaultAsync(s => s.ProductId == comp.ComponentProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                            if (compStockItem != null)
                            {
                                compStockItem.QuantityReserved = Math.Max(0, compStockItem.QuantityReserved - compReleaseQty);
                                var compProd = await _context.Products.FindAsync(new object[] { comp.ComponentProductId }, cancellationToken);
                                if (compProd != null)
                                {
                                    compProd.ReservedQuantity = compStockItem.QuantityReserved;
                                }

                                _context.StockMovements.Add(new StockMovement
                                {
                                    ProductId = comp.ComponentProductId,
                                    WarehouseId = warehouseId.Value,
                                    MovementType = StockMovementType.StockReservationReleased,
                                    QuantityChange = compReleaseQty,
                                    QuantityBefore = compStockItem.QuantityOnHand,
                                    QuantityAfter = compStockItem.QuantityOnHand,
                                    ReferenceType = "BundleCancellation",
                                    ReferenceId = order.OrderNumber,
                                    Reason = $"Released bundle component stock for Order #{order.OrderNumber} cancellation",
                                    CreatedBy = _currentUser.UserName ?? "Admin",
                                    CreatedAtUtc = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(order.CouponCode))
            {
                var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code.ToUpper() == order.CouponCode.Trim().ToUpper(), cancellationToken);
                if (promo != null && promo.UsedCount > 0)
                {
                    promo.UsedCount--;
                    promo.RowVersion = Guid.NewGuid();
                }

                var redemptions = await _context.PromotionRedemptions
                    .Where(r => r.OrderId == order.Id && !r.IsDeleted)
                    .ToListAsync(cancellationToken);
                foreach (var r in redemptions)
                {
                    r.IsDeleted = true;
                    r.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            foreach (var inv in order.Invoices)
            {
                inv.Status = InvoiceStatus.Cancelled;
            }
        }
        else if (request.NewStatus is OrderStatus.Shipped or OrderStatus.Delivered && oldStatus is not (OrderStatus.Shipped or OrderStatus.OutForDelivery or OrderStatus.Delivered))
        {
            // Fulfill reserved stock into permanent deduction
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var products = await _context.Products
                .Include(p => p.BundleComponents)
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);

                if (warehouseId.HasValue)
                {
                    var stockItem = await _context.StockItems
                        .FirstOrDefaultAsync(s => s.ProductId == item.ProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                    if (stockItem != null)
                    {
                        var beforeOnHand = stockItem.QuantityOnHand;
                        stockItem.QuantityOnHand = Math.Max(0, stockItem.QuantityOnHand - item.Quantity);
                        stockItem.QuantityReserved = Math.Max(0, stockItem.QuantityReserved - item.Quantity);

                        if (prod != null)
                        {
                            prod.StockQuantity = stockItem.QuantityOnHand;
                            prod.ReservedQuantity = stockItem.QuantityReserved;
                        }

                        _context.StockMovements.Add(new StockMovement
                        {
                            ProductId = item.ProductId,
                            WarehouseId = warehouseId.Value,
                            MovementType = StockMovementType.Sale,
                            QuantityChange = -item.Quantity,
                            QuantityBefore = beforeOnHand,
                            QuantityAfter = stockItem.QuantityOnHand,
                            ReferenceType = "OrderShipped",
                            ReferenceId = order.OrderNumber,
                            Reason = $"Deducted on-hand stock for fulfilled Order #{order.OrderNumber}",
                            CreatedBy = _currentUser.UserName ?? "Admin",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }

                    // Deduct components if bundle
                    if (prod != null && prod.ProductType == ProductType.Bundle && prod.BundleComponents.Count > 0)
                    {
                        foreach (var comp in prod.BundleComponents)
                        {
                            var compDeductQty = item.Quantity * comp.Quantity;
                            var compStockItem = await _context.StockItems
                                .FirstOrDefaultAsync(s => s.ProductId == comp.ComponentProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                            if (compStockItem != null)
                            {
                                var compBeforeOnHand = compStockItem.QuantityOnHand;
                                compStockItem.QuantityOnHand = Math.Max(0, compStockItem.QuantityOnHand - compDeductQty);
                                compStockItem.QuantityReserved = Math.Max(0, compStockItem.QuantityReserved - compDeductQty);
                                var compProd = await _context.Products.FindAsync(new object[] { comp.ComponentProductId }, cancellationToken);
                                if (compProd != null)
                                {
                                    compProd.StockQuantity = compStockItem.QuantityOnHand;
                                    compProd.ReservedQuantity = compStockItem.QuantityReserved;
                                }

                                _context.StockMovements.Add(new StockMovement
                                {
                                    ProductId = comp.ComponentProductId,
                                    WarehouseId = warehouseId.Value,
                                    MovementType = StockMovementType.Sale,
                                    QuantityChange = -compDeductQty,
                                    QuantityBefore = compBeforeOnHand,
                                    QuantityAfter = compStockItem.QuantityOnHand,
                                    ReferenceType = "BundleShipped",
                                    ReferenceId = order.OrderNumber,
                                    Reason = $"Deducted bundle component stock for fulfilled Order #{order.OrderNumber}",
                                    CreatedBy = _currentUser.UserName ?? "Admin",
                                    CreatedAtUtc = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
            }
        }
        // Note: Returned status does NOT auto-restock; restock only happens via Sellable Return Inspection in Phase 7.

        if (request.NewStatus == OrderStatus.Delivered && order.PaymentMethod == PaymentMethod.COD)
        {
            order.PaymentStatus = PaymentStatus.Paid;
            foreach (var inv in order.Invoices)
            {
                inv.Status = InvoiceStatus.Paid;
                inv.PaidAmount = inv.GrandTotal;
                inv.BalanceAmount = Money.Zero();
            }
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.OrderStatusChanged,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            before: new { Status = oldStatus.ToString() },
            after: new { Status = order.OrderStatus.ToString() },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("OrderStatusChanged", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.OrderStatus.ToString(),
            CustomerEmail = order.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    public async Task<PagedResult<OrderDto>> GetOrdersAsync(int page = 1, int pageSize = 20, OrderStatus? status = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Invoices)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(o => o.OrderStatus == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(s) ||
                (o.Customer != null && (o.Customer.FirstName.ToLower().Contains(s) || o.Customer.LastName.ToLower().Contains(s) || o.Customer.Phone.Contains(s))) ||
                (o.UtrNumber != null && o.UtrNumber.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.PlacedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => MapToOrderDto(o))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderDto>(items, totalCount, page, pageSize);
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order == null ? null : MapToOrderDto(order);
    }

    public async Task<OrderDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.Trim(), cancellationToken);

        return order == null ? null : MapToOrderDto(order);
    }

    public async Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Invoices)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAtUtc)
            .Select(o => MapToOrderDto(o))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> GetOrdersByCustomerIdAsync(Guid customerId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Invoices)
            .Where(o => o.CustomerId == customerId)
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.PlacedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => MapToOrderDto(o))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderDto>(items, totalCount, page, pageSize);
    }

    public async Task<OrderTrackingDto?> TrackOrderAsync(string orderNumberOrPhone, CancellationToken cancellationToken = default)
    {
        var q = orderNumberOrPhone.Trim().ToUpper();
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .FirstOrDefaultAsync(o => o.OrderNumber == q || o.TrackingNumber == q, cancellationToken);

        if (order == null) return null;

        return new OrderTrackingDto
        {
            OrderNumber = order.OrderNumber,
            Status = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            PaymentMethod = order.PaymentMethod,
            UtrNumber = order.UtrNumber,
            PaymentScreenshotUrl = order.PaymentScreenshotUrl,
            PlacedAtUtc = order.PlacedAtUtc,
            EstimatedDeliveryUtc = order.PlacedAtUtc.AddDays(3),
            TrackingNumber = order.TrackingNumber,
            DeliveryAddressSummary = order.ShippingAddress.ToSingleLine(),
            Timeline = order.StatusHistories.OrderBy(h => h.ChangedAtUtc).Select(h => new OrderStatusHistoryDto
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Reason = h.Reason,
                ChangedBy = h.ChangedBy,
                ChangedAtUtc = h.ChangedAtUtc
            }).ToList(),
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                SKU = i.SKUSnapshot,
                ImageUrl = i.ProductImageUrlSnapshot,
                UnitPrice = i.UnitPrice.ToDecimal(),
                Quantity = i.Quantity,
                Discount = i.Discount.ToDecimal(),
                Tax = i.Tax.ToDecimal(),
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList(),
            GrandTotal = order.GrandTotal.ToDecimal()
        };
    }

    public async Task<OrderDto> SubmitPaymentProofAsync(Guid orderId, SubmitPaymentProofRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        order.UtrNumber = request.UtrNumber.Trim();
        order.PaymentScreenshotUrl = request.PaymentScreenshotUrl ?? request.PaymentScreenshotBase64;
        order.PaymentSubmittedAtUtc = DateTime.UtcNow;
        order.PaymentVerificationNotes = request.Notes;

        order.StatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.OrderStatus,
            ToStatus = order.OrderStatus,
            Reason = $"Payment proof submitted. UTR: {request.UtrNumber}",
            ChangedBy = _currentUser.UserName ?? "Customer",
            ChangedAtUtc = DateTime.UtcNow
        });

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            after: new { order.UtrNumber, order.PaymentSubmittedAtUtc },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    public async Task<OrderDto> VerifyPaymentAsync(Guid orderId, VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            throw new DomainException("Cannot verify payment for a cancelled order.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new DomainException("Payment is already verified for this order.");
        }

        var oldPaymentStatus = order.PaymentStatus;
        var oldOrderStatus = order.OrderStatus;

        order.PaymentStatus = PaymentStatus.Paid;
        order.PaymentVerifiedAtUtc = DateTime.UtcNow;
        order.PaymentVerifiedBy = _currentUser.UserName ?? _currentUser.Email ?? "Admin";
        order.PaymentVerificationNotes = request.VerificationNotes ?? "UPI Payment Proof & UTR Verified.";
        if (!string.IsNullOrWhiteSpace(request.VerifiedUtrNumber))
        {
            order.UtrNumber = request.VerifiedUtrNumber;
        }

        // Transition OrderStatus to Confirmed if Pending
        if (order.OrderStatus == OrderStatus.Pending)
        {
            order.ChangeStatus(OrderStatus.Confirmed, "Payment verified by Admin", order.PaymentVerifiedBy);
        }

        // Create Payment record
        var paymentNumber = await _numberGenerator.GeneratePaymentNumberAsync(cancellationToken);
        var payment = new Payment
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            PaymentNumber = paymentNumber,
            Amount = order.GrandTotal,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = PaymentStatus.Paid,
            TransactionReference = order.UtrNumber ?? "UPI-QR",
            UtrNumber = order.UtrNumber,
            Notes = $"Verified by {order.PaymentVerifiedBy}",
            PaidAtUtc = DateTime.UtcNow
        };
        _context.Payments.Add(payment);

        // Update invoices to Paid
        foreach (var inv in order.Invoices)
        {
            inv.Status = InvoiceStatus.Paid;
            inv.PaidAmount = inv.GrandTotal;
            inv.BalanceAmount = Money.Zero();
        }

        if (request.AutoMoveToPacking && order.CanTransitionTo(OrderStatus.Processing))
        {
            order.ChangeStatus(OrderStatus.Processing, "Auto moved to packing station upon payment verification", order.PaymentVerifiedBy);
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PaymentVerified,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            before: new { PaymentStatus = oldPaymentStatus.ToString(), OrderStatus = oldOrderStatus.ToString() },
            after: new { PaymentStatus = order.PaymentStatus.ToString(), OrderStatus = order.OrderStatus.ToString(), order.UtrNumber },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("PaymentVerified", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            order.CustomerId,
            CustomerEmail = order.Customer?.Email,
            GrandTotal = order.GrandTotal.ToDecimal(),
            order.UtrNumber
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    public async Task<OrderDto> MoveToPackingAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        var operatorName = _currentUser.UserName ?? _currentUser.Email ?? "Warehouse Packing Team";
        var oldStatus = order.OrderStatus;

        if (order.OrderStatus == OrderStatus.Confirmed)
        {
            order.ChangeStatus(OrderStatus.Processing, "Moved to Packing Station", operatorName);
        }
        else if (order.OrderStatus == OrderStatus.Processing)
        {
            order.ChangeStatus(OrderStatus.Packed, "Order Packed in Fire-Safe Cartons", operatorName);
        }
        else
        {
            throw new InvalidOrderStateTransitionException(order.OrderStatus.ToString(), "Processing/Packed");
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.OrderStatusChanged,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            before: new { Status = oldStatus.ToString() },
            after: new { Status = order.OrderStatus.ToString(), Operator = operatorName },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("OrderStatusChanged", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.OrderStatus.ToString(),
            CustomerEmail = order.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    public async Task<OrderDto> RejectPaymentAsync(Guid orderId, RejectPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            throw new DomainException("Order is already cancelled.");
        }

        var operatorName = _currentUser.UserName ?? _currentUser.Email ?? "Admin";
        var oldPaymentStatus = order.PaymentStatus;
        var oldOrderStatus = order.OrderStatus;

        order.PaymentStatus = PaymentStatus.Failed;
        order.PaymentVerificationNotes = $"REJECTED: {request.Reason}";
        order.ChangeStatus(OrderStatus.Cancelled, $"Payment Proof Rejected: {request.Reason}", operatorName);

        // Release reserved stock at warehouse and product level
        var warehouseId = order.WarehouseId;
        if (!warehouseId.HasValue)
        {
            var primaryWh = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && w.IsActive, cancellationToken)
                ?? await _context.Warehouses.FirstOrDefaultAsync(w => w.IsActive, cancellationToken);
            warehouseId = primaryWh?.Id;
        }

        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products
            .Include(p => p.BundleComponents)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var prod = products.FirstOrDefault(p => p.Id == item.ProductId);

            if (warehouseId.HasValue)
            {
                var stockItem = await _context.StockItems
                    .FirstOrDefaultAsync(s => s.ProductId == item.ProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                if (stockItem != null)
                {
                    stockItem.QuantityReserved = Math.Max(0, stockItem.QuantityReserved - item.Quantity);
                    if (prod != null)
                    {
                        prod.StockQuantity = stockItem.QuantityOnHand;
                        prod.ReservedQuantity = stockItem.QuantityReserved;
                    }

                    _context.StockMovements.Add(new StockMovement
                    {
                        ProductId = item.ProductId,
                        WarehouseId = warehouseId.Value,
                        MovementType = StockMovementType.StockReservationReleased,
                        QuantityChange = item.Quantity,
                        QuantityBefore = stockItem.QuantityOnHand,
                        QuantityAfter = stockItem.QuantityOnHand,
                        ReferenceType = "PaymentRejected",
                        ReferenceId = order.OrderNumber,
                        Reason = $"Released reserved stock due to payment rejection for Order #{order.OrderNumber}",
                        CreatedBy = operatorName,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                // Release components if bundle
                if (prod != null && prod.ProductType == ProductType.Bundle && prod.BundleComponents.Count > 0)
                {
                    foreach (var comp in prod.BundleComponents)
                    {
                        var compReleaseQty = item.Quantity * comp.Quantity;
                        var compStockItem = await _context.StockItems
                            .FirstOrDefaultAsync(s => s.ProductId == comp.ComponentProductId && s.WarehouseId == warehouseId.Value, cancellationToken);
                        if (compStockItem != null)
                        {
                            compStockItem.QuantityReserved = Math.Max(0, compStockItem.QuantityReserved - compReleaseQty);
                            var compProd = await _context.Products.FindAsync(new object[] { comp.ComponentProductId }, cancellationToken);
                            if (compProd != null)
                            {
                                compProd.ReservedQuantity = compStockItem.QuantityReserved;
                            }

                            _context.StockMovements.Add(new StockMovement
                            {
                                ProductId = comp.ComponentProductId,
                                WarehouseId = warehouseId.Value,
                                MovementType = StockMovementType.StockReservationReleased,
                                QuantityChange = compReleaseQty,
                                QuantityBefore = compStockItem.QuantityOnHand,
                                QuantityAfter = compStockItem.QuantityOnHand,
                                ReferenceType = "BundlePaymentRejected",
                                ReferenceId = order.OrderNumber,
                                Reason = $"Released bundle component stock for Order #{order.OrderNumber} payment rejection",
                                CreatedBy = operatorName,
                                CreatedAtUtc = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code.ToUpper() == order.CouponCode.Trim().ToUpper(), cancellationToken);
            if (promo != null && promo.UsedCount > 0)
            {
                promo.UsedCount--;
                promo.RowVersion = Guid.NewGuid();
            }

            var redemptions = await _context.PromotionRedemptions
                .Where(r => r.OrderId == order.Id && !r.IsDeleted)
                .ToListAsync(cancellationToken);
            foreach (var r in redemptions)
            {
                r.IsDeleted = true;
                r.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        foreach (var inv in order.Invoices)
        {
            inv.Status = InvoiceStatus.Cancelled;
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PaymentRejected,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            before: new { PaymentStatus = oldPaymentStatus.ToString(), OrderStatus = oldOrderStatus.ToString() },
            after: new { PaymentStatus = order.PaymentStatus.ToString(), OrderStatus = order.OrderStatus.ToString(), Reason = request.Reason },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("PaymentRejected", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            Reason = request.Reason,
            CustomerEmail = order.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    private static OrderDto MapToOrderDto(Order o)
    {
        return new OrderDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            CustomerId = o.CustomerId,
            CustomerName = o.Customer != null ? $"{o.Customer.FirstName} {o.Customer.LastName}".Trim() : "Guest",
            CustomerEmail = o.Customer?.Email ?? string.Empty,
            CustomerPhone = o.Customer?.Phone ?? string.Empty,
            OrderStatus = o.OrderStatus,
            PaymentStatus = o.PaymentStatus,
            PaymentMethod = o.PaymentMethod,
            FulfillmentStatus = o.FulfillmentStatus,
            ItemsSubtotal = o.ItemsSubtotal.ToDecimal(),
            Discount = o.Discount.ToDecimal(),
            Tax = o.Tax.ToDecimal(),
            ShippingCharge = o.ShippingCharge.ToDecimal(),
            GrandTotal = o.GrandTotal.ToDecimal(),
            CouponCode = o.CouponCode,
            Notes = o.Notes,
            TrackingNumber = o.TrackingNumber,
            PlacedAtUtc = o.PlacedAtUtc,
            UtrNumber = o.UtrNumber,
            PaymentScreenshotUrl = o.PaymentScreenshotUrl,
            PaymentSubmittedAtUtc = o.PaymentSubmittedAtUtc,
            PaymentVerifiedAtUtc = o.PaymentVerifiedAtUtc,
            PaymentVerifiedBy = o.PaymentVerifiedBy,
            PaymentVerificationNotes = o.PaymentVerificationNotes,
            ShippingAddress = new CustomerAddressDto
            {
                FullName = o.ShippingAddress.FullName,
                Phone = o.ShippingAddress.Phone,
                AddressLine1 = o.ShippingAddress.AddressLine1,
                AddressLine2 = o.ShippingAddress.AddressLine2,
                City = o.ShippingAddress.City,
                State = o.ShippingAddress.State,
                PostalCode = o.ShippingAddress.PostalCode,
                Country = o.ShippingAddress.Country
            },
            Items = o.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                SKU = i.SKUSnapshot,
                ImageUrl = i.ProductImageUrlSnapshot,
                UnitPrice = i.UnitPrice.ToDecimal(),
                Quantity = i.Quantity,
                Discount = i.Discount.ToDecimal(),
                Tax = i.Tax.ToDecimal(),
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList(),
            StatusHistories = o.StatusHistories.OrderBy(h => h.ChangedAtUtc).Select(h => new OrderStatusHistoryDto
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Reason = h.Reason,
                ChangedBy = h.ChangedBy,
                ChangedAtUtc = h.ChangedAtUtc
            }).ToList()
        };
    }

    public async Task<ReturnOrderDto> CreateReturnOrderAsync(CreateReturnOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            throw new DomainException("Return request must contain at least one item.");

        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), request.OrderId);

        if (order.OrderStatus is not (OrderStatus.Delivered or OrderStatus.Shipped or OrderStatus.Returned))
            throw new DomainException($"Cannot request a return for an order with status '{order.OrderStatus}'. Returns are only permitted for shipped or delivered orders.");

        var returnNumber = await _numberGenerator.GenerateReturnNumberAsync(cancellationToken);
        var returnOrder = new ReturnOrder
        {
            ReturnNumber = returnNumber,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Reason = request.Reason,
            Status = "Requested",
            RequestedAtUtc = DateTime.UtcNow
        };

        decimal expectedRefund = 0;
        foreach (var reqItem in request.Items)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.ProductId == reqItem.ProductId);
            if (orderItem == null)
                throw new DomainException($"Product {reqItem.ProductId} was not found in Order #{order.OrderNumber}.");

            if (reqItem.Quantity <= 0 || reqItem.Quantity > orderItem.Quantity)
                throw new DomainException($"Invalid return quantity {reqItem.Quantity} for product '{orderItem.ProductNameSnapshot}'. Maximum allowed is {orderItem.Quantity}.");

            var returnItem = new ReturnOrderItem
            {
                ReturnOrderId = returnOrder.Id,
                ProductId = reqItem.ProductId,
                Quantity = reqItem.Quantity,
                UnitPrice = orderItem.UnitPrice,
                ConditionNotes = reqItem.Reason
            };
            returnOrder.Items.Add(returnItem);
            expectedRefund += orderItem.UnitPrice.ToDecimal() * reqItem.Quantity;
        }

        returnOrder.RefundAmount = Money.FromDecimal(expectedRefund);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);
        _context.ReturnOrders.Add(returnOrder);

        await _auditLog.LogAsync(
            AuditAction.ReturnRequested,
            "Returns",
            nameof(ReturnOrder),
            returnOrder.Id.ToString(),
            returnOrder.ReturnNumber,
            after: new { returnOrder.ReturnNumber, returnOrder.OrderId, returnOrder.Reason, RefundAmount = returnOrder.RefundAmount.ToDecimal() },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("ReturnRequested", new
        {
            ReturnId = returnOrder.Id,
            returnOrder.ReturnNumber,
            order.OrderNumber,
            CustomerEmail = order.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetReturnOrderByIdAsync(returnOrder.Id, cancellationToken))!;
    }

    public async Task<ReturnOrderDto> ApproveReturnOrderAsync(Guid returnId, string? notes = null, CancellationToken cancellationToken = default)
    {
        var returnOrder = await _context.ReturnOrders
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(ReturnOrder), returnId);

        if (returnOrder.Status != "Requested")
            throw new DomainException($"Cannot approve return with status '{returnOrder.Status}'. Only 'Requested' returns can be approved.");

        returnOrder.Status = "Approved";
        returnOrder.InspectionNotes = notes;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.ReturnApproved,
            "Returns",
            nameof(ReturnOrder),
            returnOrder.Id.ToString(),
            returnOrder.ReturnNumber,
            after: new { returnOrder.ReturnNumber, returnOrder.Status, Notes = notes },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("ReturnApproved", new
        {
            ReturnId = returnOrder.Id,
            returnOrder.ReturnNumber,
            CustomerEmail = returnOrder.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetReturnOrderByIdAsync(returnOrder.Id, cancellationToken))!;
    }

    public async Task<ReturnOrderDto> ReceiveReturnOrderAsync(Guid returnId, string? notes = null, CancellationToken cancellationToken = default)
    {
        var returnOrder = await _context.ReturnOrders
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(ReturnOrder), returnId);

        if (returnOrder.Status != "Approved")
            throw new DomainException($"Cannot mark return as received with status '{returnOrder.Status}'. Return must first be 'Approved'.");

        returnOrder.Status = "Received";
        if (!string.IsNullOrWhiteSpace(notes))
        {
            returnOrder.InspectionNotes = string.IsNullOrWhiteSpace(returnOrder.InspectionNotes) ? notes : $"{returnOrder.InspectionNotes}; {notes}";
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.ReturnReceived,
            "Returns",
            nameof(ReturnOrder),
            returnOrder.Id.ToString(),
            returnOrder.ReturnNumber,
            after: new { returnOrder.ReturnNumber, returnOrder.Status },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetReturnOrderByIdAsync(returnOrder.Id, cancellationToken))!;
    }

    public async Task<ReturnOrderDto> InspectReturnOrderAsync(Guid returnId, InspectReturnOrderRequest request, CancellationToken cancellationToken = default)
    {
        var returnOrder = await _context.ReturnOrders
            .Include(r => r.Items)
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(ReturnOrder), returnId);

        if (returnOrder.Status is not ("Received" or "Approved"))
            throw new DomainException($"Cannot inspect return in status '{returnOrder.Status}'. Return must be 'Received' or 'Approved'.");

        returnOrder.InspectionNotes = request.InspectionNotes;
        returnOrder.InspectedAtUtc = DateTime.UtcNow;
        returnOrder.Status = "Inspected";

        var warehouseId = returnOrder.Order.WarehouseId
            ?? (await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && w.IsActive, cancellationToken))?.Id
            ?? (await _context.Warehouses.FirstOrDefaultAsync(w => w.IsActive, cancellationToken))?.Id
            ?? throw new DomainException("No active warehouse found for restocking return items.");

        bool anySellable = false;
        decimal totalSellableRefund = 0;

        foreach (var itemInspection in request.ItemInspections)
        {
            var returnItem = returnOrder.Items.FirstOrDefault(i => i.ProductId == itemInspection.ProductId);
            if (returnItem != null)
            {
                returnItem.IsDamaged = !itemInspection.IsSellable;
                returnItem.ConditionNotes = itemInspection.ConditionNotes;

                if (itemInspection.IsSellable && itemInspection.Quantity > 0)
                {
                    anySellable = true;
                    totalSellableRefund += returnItem.UnitPrice.ToDecimal() * itemInspection.Quantity;

                    // Restock ONLY sellable items into StockItem authoritative ledger
                    var stockItem = await _context.StockItems
                        .FirstOrDefaultAsync(s => s.ProductId == itemInspection.ProductId && s.WarehouseId == warehouseId, cancellationToken);
                    var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == itemInspection.ProductId, cancellationToken);

                    if (stockItem != null)
                    {
                        var beforeOnHand = stockItem.QuantityOnHand;
                        stockItem.QuantityOnHand += itemInspection.Quantity;
                        if (product != null)
                        {
                            product.StockQuantity = stockItem.QuantityOnHand;
                        }

                        _context.StockMovements.Add(new StockMovement
                        {
                            ProductId = itemInspection.ProductId,
                            WarehouseId = warehouseId,
                            MovementType = StockMovementType.Return,
                            QuantityChange = itemInspection.Quantity,
                            QuantityBefore = beforeOnHand,
                            QuantityAfter = stockItem.QuantityOnHand,
                            ReferenceType = "ReturnInspection",
                            ReferenceId = returnOrder.ReturnNumber,
                            Reason = $"Restocked sellable returned items from Return #{returnOrder.ReturnNumber}",
                            CreatedBy = _currentUser.UserName ?? "Inspector",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }
                else if (!itemInspection.IsSellable && itemInspection.Quantity > 0)
                {
                    // Damaged item log
                    var currentStock = (await _context.StockItems.FirstOrDefaultAsync(s => s.ProductId == itemInspection.ProductId && s.WarehouseId == warehouseId, cancellationToken))?.QuantityOnHand ?? 0;
                    _context.StockMovements.Add(new StockMovement
                    {
                        ProductId = itemInspection.ProductId,
                        WarehouseId = warehouseId,
                        MovementType = StockMovementType.Damage,
                        QuantityChange = 0,
                        QuantityBefore = currentStock,
                        QuantityAfter = currentStock,
                        ReferenceType = "ReturnInspectionDamaged",
                        ReferenceId = returnOrder.ReturnNumber,
                        Reason = $"Damaged returned items flagged from Return #{returnOrder.ReturnNumber}: {itemInspection.ConditionNotes}",
                        CreatedBy = _currentUser.UserName ?? "Inspector",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        returnOrder.IsSellable = anySellable;
        returnOrder.RefundAmount = Money.FromDecimal(totalSellableRefund);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.ReturnInspected,
            "Returns",
            nameof(ReturnOrder),
            returnOrder.Id.ToString(),
            returnOrder.ReturnNumber,
            after: new { returnOrder.ReturnNumber, returnOrder.Status, returnOrder.IsSellable, RefundAmount = returnOrder.RefundAmount.ToDecimal() },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("ReturnInspected", new
        {
            ReturnId = returnOrder.Id,
            returnOrder.ReturnNumber,
            returnOrder.IsSellable,
            RefundAmount = returnOrder.RefundAmount.ToDecimal(),
            CustomerEmail = returnOrder.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetReturnOrderByIdAsync(returnOrder.Id, cancellationToken))!;
    }

    public async Task<PagedResult<ReturnOrderDto>> GetReturnOrdersAsync(int page = 1, int pageSize = 20, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ReturnOrders
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status.ToLower() == status.Trim().ToLower());

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReturnOrderDto
            {
                Id = r.Id,
                ReturnNumber = r.ReturnNumber,
                OrderId = r.OrderId,
                OrderNumber = r.Order.OrderNumber,
                CustomerId = r.CustomerId,
                CustomerName = $"{r.Customer.FirstName} {r.Customer.LastName}".Trim(),
                Reason = r.Reason,
                Status = r.Status,
                InspectionNotes = r.InspectionNotes,
                IsSellable = r.IsSellable,
                RefundAmount = r.RefundAmount.ToDecimal(),
                RequestedAtUtc = r.RequestedAtUtc,
                InspectedAtUtc = r.InspectedAtUtc,
                Items = r.Items.Select(i => new ReturnOrderItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product.Name,
                    SKU = i.Product.SKU,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice.ToDecimal(),
                    IsDamaged = i.IsDamaged,
                    ConditionNotes = i.ConditionNotes
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ReturnOrderDto>(items, totalCount, page, pageSize);
    }

    public async Task<ReturnOrderDto?> GetReturnOrderByIdAsync(Guid returnId, CancellationToken cancellationToken = default)
    {
        var r = await _context.ReturnOrders
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(r => r.Id == returnId && !r.IsDeleted, cancellationToken);

        if (r == null) return null;

        return new ReturnOrderDto
        {
            Id = r.Id,
            ReturnNumber = r.ReturnNumber,
            OrderId = r.OrderId,
            OrderNumber = r.Order.OrderNumber,
            CustomerId = r.CustomerId,
            CustomerName = $"{r.Customer.FirstName} {r.Customer.LastName}".Trim(),
            Reason = r.Reason,
            Status = r.Status,
            InspectionNotes = r.InspectionNotes,
            IsSellable = r.IsSellable,
            RefundAmount = r.RefundAmount.ToDecimal(),
            RequestedAtUtc = r.RequestedAtUtc,
            InspectedAtUtc = r.InspectedAtUtc,
            Items = r.Items.Select(i => new ReturnOrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                SKU = i.Product.SKU,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice.ToDecimal(),
                IsDamaged = i.IsDamaged,
                ConditionNotes = i.ConditionNotes
            }).ToList()
        };
    }
}
