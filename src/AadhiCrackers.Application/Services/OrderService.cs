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
    Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<OrderTrackingDto?> TrackOrderAsync(string orderNumberOrPhone, CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IOutboxService _outbox;

    public OrderService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IOutboxService outbox)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _outbox = outbox;
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
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Get primary warehouse
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && !w.IsDeleted, cancellationToken)
            ?? await _context.Warehouses.FirstOrDefaultAsync(w => !w.IsDeleted, cancellationToken);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Images)
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var orderNumber = await GenerateOrderNumberAsync(cancellationToken);

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            WarehouseId = warehouse?.Id,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = request.PaymentMethod == PaymentMethod.COD ? PaymentStatus.Pending : PaymentStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            CouponCode = request.CouponCode,
            Notes = request.Notes,
            PlacedAtUtc = DateTime.UtcNow,
            UtrNumber = request.UtrNumber,
            PaymentScreenshotUrl = request.PaymentScreenshotUrl ?? request.PaymentScreenshotBase64,
            PaymentSubmittedAtUtc = !string.IsNullOrWhiteSpace(request.UtrNumber) || !string.IsNullOrWhiteSpace(request.PaymentScreenshotUrl) || !string.IsNullOrWhiteSpace(request.PaymentScreenshotBase64) ? DateTime.UtcNow : null,
            ShippingAddress = request.ShippingAddress,
            BillingAddress = request.BillingAddress ?? request.ShippingAddress,
            TrackingNumber = $"TRK-{Random.Shared.Next(10000000, 99999999)}"
        };

        var itemsSubtotal = Money.Zero();
        var totalTax = Money.Zero();

        foreach (var reqItem in request.Items)
        {
            if (!products.TryGetValue(reqItem.ProductId, out var product))
                throw new ResourceNotFoundException(nameof(Product), reqItem.ProductId);

            // Stock Check
            if (product.AvailableQuantity < reqItem.Quantity)
            {
                throw new InsufficientStockException(product.SKU, product.AvailableQuantity, reqItem.Quantity);
            }

            // Reserve stock
            product.ReservedQuantity += reqItem.Quantity;

            // Update warehouse stock item if warehouse exists
            if (warehouse != null)
            {
                var stockItem = await _context.StockItems
                    .FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == warehouse.Id, cancellationToken);

                if (stockItem != null)
                {
                    stockItem.QuantityReserved += reqItem.Quantity;
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
                Quantity = reqItem.Quantity,
                Discount = Money.Zero(),
                Tax = itemTax,
                LineTotal = lineTotalBeforeTax
            };

            order.Items.Add(orderItem);
            itemsSubtotal += lineTotalBeforeTax;
            totalTax += itemTax;

            // Generate stock movement reservation
            if (warehouse != null)
            {
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = -reqItem.Quantity,
                    QuantityBefore = product.StockQuantity,
                    QuantityAfter = product.StockQuantity,
                    ReferenceType = "OrderReservation",
                    ReferenceId = order.OrderNumber,
                    Reason = $"Stock reserved for Order #{order.OrderNumber}"
                });
            }
        }

        order.ItemsSubtotal = itemsSubtotal;
        order.Tax = totalTax;

        // Apply Promotion/Coupon if specified
        var discount = Money.Zero();
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code.ToUpper() == request.CouponCode.Trim().ToUpper(), cancellationToken);
            if (promo != null && promo.IsValidForOrder(itemsSubtotal))
            {
                discount = promo.CalculateDiscount(itemsSubtotal);
                promo.UsedCount++;
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
        var invoiceNumber = await GenerateInvoiceNumberAsync(cancellationToken);
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
        await _context.SaveChangesAsync(cancellationToken);

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

        // Handle Cancellation -> Release reserved stock
        if (request.NewStatus == OrderStatus.Cancelled)
        {
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var products = await _context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null)
                {
                    prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
                }

                if (order.WarehouseId.HasValue)
                {
                    var stockItem = await _context.StockItems
                        .FirstOrDefaultAsync(s => s.ProductId == item.ProductId && s.WarehouseId == order.WarehouseId.Value, cancellationToken);
                    if (stockItem != null)
                    {
                        stockItem.QuantityReserved = Math.Max(0, stockItem.QuantityReserved - item.Quantity);
                    }

                    _context.StockMovements.Add(new StockMovement
                    {
                        ProductId = item.ProductId,
                        WarehouseId = order.WarehouseId.Value,
                        MovementType = StockMovementType.Adjustment,
                        QuantityChange = item.Quantity,
                        QuantityBefore = prod?.StockQuantity ?? 0,
                        QuantityAfter = prod?.StockQuantity ?? 0,
                        ReferenceType = "OrderCancellation",
                        ReferenceId = order.OrderNumber,
                        Reason = $"Released reserved stock due to Order #{order.OrderNumber} cancellation"
                    });
                }
            }
        }
        else if (request.NewStatus is OrderStatus.Shipped or OrderStatus.Delivered && oldStatus is not (OrderStatus.Shipped or OrderStatus.Delivered))
        {
            // Fulfill reserved stock into permanent deduction
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var products = await _context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null)
                {
                    var before = prod.StockQuantity;
                    prod.StockQuantity = Math.Max(0, prod.StockQuantity - item.Quantity);
                    prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);

                    if (order.WarehouseId.HasValue)
                    {
                        var stockItem = await _context.StockItems
                            .FirstOrDefaultAsync(s => s.ProductId == item.ProductId && s.WarehouseId == order.WarehouseId.Value, cancellationToken);
                        if (stockItem != null)
                        {
                            stockItem.QuantityOnHand = Math.Max(0, stockItem.QuantityOnHand - item.Quantity);
                            stockItem.QuantityReserved = Math.Max(0, stockItem.QuantityReserved - item.Quantity);
                        }

                        _context.StockMovements.Add(new StockMovement
                        {
                            ProductId = item.ProductId,
                            WarehouseId = order.WarehouseId.Value,
                            MovementType = StockMovementType.Sale,
                            QuantityChange = -item.Quantity,
                            QuantityBefore = before,
                            QuantityAfter = prod.StockQuantity,
                            ReferenceType = "OrderShipped",
                            ReferenceId = order.OrderNumber,
                            Reason = $"Deducted on-hand stock for fulfilled Order #{order.OrderNumber}"
                        });
                    }
                }
            }
        }

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

        await _context.SaveChangesAsync(cancellationToken);

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

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated order");
    }

    public async Task<PagedResult<OrderDto>> GetOrdersAsync(int page = 1, int pageSize = 20, OrderStatus? status = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.OrderStatus == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(s) ||
                o.Customer.FirstName.ToLower().Contains(s) ||
                o.Customer.LastName.ToLower().Contains(s) ||
                o.Customer.Email.ToLower().Contains(s) ||
                o.Customer.Phone.ToLower().Contains(s));
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
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order == null ? null : MapToOrderDto(order);
    }

    public async Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAtUtc)
            .Select(o => MapToOrderDto(o))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderTrackingDto?> TrackOrderAsync(string orderNumberOrPhone, CancellationToken cancellationToken = default)
    {
        var q = orderNumberOrPhone.Trim().ToLower();
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .FirstOrDefaultAsync(o => o.OrderNumber.ToLower() == q || o.Customer.Phone.ToLower() == q, cancellationToken);

        if (order == null) return null;

        return new OrderTrackingDto
        {
            OrderNumber = order.OrderNumber,
            Status = order.OrderStatus,
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

    public async Task<OrderDto> VerifyPaymentAsync(Guid orderId, VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

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
        var payment = new Payment
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            PaymentNumber = $"PAY-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}",
            Amount = order.GrandTotal,
            PaymentMethod = PaymentMethod.UPI,
            PaymentStatus = PaymentStatus.Paid,
            TransactionReference = order.UtrNumber ?? "UPI-QR",
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

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PaymentCreated,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            before: new { PaymentStatus = oldPaymentStatus.ToString(), OrderStatus = oldOrderStatus.ToString() },
            after: new { PaymentStatus = order.PaymentStatus.ToString(), OrderStatus = order.OrderStatus.ToString(), order.UtrNumber },
            cancellationToken: cancellationToken);

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

        if (order.OrderStatus == OrderStatus.Confirmed)
        {
            order.ChangeStatus(OrderStatus.Processing, "Moved to Packing Station", operatorName);
        }
        else if (order.OrderStatus == OrderStatus.Processing)
        {
            order.ChangeStatus(OrderStatus.Packed, "Order Packed in Fire-Safe Cartons", operatorName);
        }

        await _context.SaveChangesAsync(cancellationToken);
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

        var operatorName = _currentUser.UserName ?? _currentUser.Email ?? "Admin";

        order.PaymentStatus = PaymentStatus.Failed;
        order.PaymentVerificationNotes = $"REJECTED: {request.Reason}";
        order.ChangeStatus(OrderStatus.Cancelled, $"Payment Proof Rejected: {request.Reason}", operatorName);

        // Release reserved stock
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (prod != null)
            {
                prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
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

    private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
    {
        var count = await _context.Orders.CountAsync(cancellationToken) + 1;
        return $"ORD-{DateTime.UtcNow:yyyy}-{count:D6}";
    }

    private async Task<string> GenerateInvoiceNumberAsync(CancellationToken cancellationToken)
    {
        var count = await _context.Invoices.CountAsync(cancellationToken) + 1;
        return $"INV-{DateTime.UtcNow:yyyy}-{count:D6}";
    }
}
