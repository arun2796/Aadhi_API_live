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
    Task<OrderDto> DispatchOrderAsync(Guid orderId, DispatchOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> VerifyPaymentAsync(Guid orderId, VerifyPaymentRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> MoveToPackingAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<OrderDto> RejectPaymentAsync(Guid orderId, RejectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetOrdersAsync(int page = 1, int pageSize = 20, OrderStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<List<OrderDto>> GetCustomerOrdersAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<List<OrderDto>> GetMyOrdersAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetOrdersByCustomerIdAsync(Guid customerId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<OrderTrackingDto?> TrackOrderAsync(string orderNumberOrPhone, CancellationToken cancellationToken = default);
    Task<OrderDto> SubmitPaymentProofAsync(Guid orderId, SubmitPaymentProofRequest request, CancellationToken cancellationToken = default);
    Task<List<DeliveryOptionDto>> GetDeliveryOptionsAsync(decimal subtotal, CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IOutboxService _outbox;
    private readonly IBusinessNumberGenerator _numberGenerator;
    private readonly IFileStorageService? _fileStorage;
    private readonly IOrderPricingService _pricing;

    public OrderService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IOutboxService outbox,
        IBusinessNumberGenerator numberGenerator,
        IFileStorageService? fileStorage = null,
        IOrderPricingService? pricing = null)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _outbox = outbox;
        _numberGenerator = numberGenerator;
        _fileStorage = fileStorage;
        _pricing = pricing ?? new OrderPricingService(context);
    }

    private Task<string?> ProcessScreenshotBase64Async(string? base64Data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(base64Data)) return Task.FromResult<string?>(null);

        var trimmed = base64Data.Trim();
        // If it's an external HTTP/HTTPS URL (e.g. S3, Cloudinary), preserve it
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(trimmed);
        }

        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(trimmed);
        }

        // If raw base64 string was passed without header, format as data URI
        try
        {
            var raw = trimmed;
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = raw.IndexOf(',');
                if (commaIndex >= 0) raw = raw[(commaIndex + 1)..];
            }
            Convert.FromBase64String(raw.Trim());
            return Task.FromResult<string?>($"data:image/jpeg;base64,{raw.Trim()}");
        }
        catch
        {
            return Task.FromResult<string?>(trimmed);
        }
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            throw new DomainException("Cannot create an order with zero items.");

        // Find or create customer
        // Same helper the cart quote uses, so the quote and the order count a coupon's
        // per-customer limit against the same customer record.
        var customerEmail = OrderPricingService.ResolveOrderCustomerEmail(_currentUser.Email);
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

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Images)
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var orderNumber = await _numberGenerator.GenerateOrderNumberAsync(cancellationToken);
        var deliveryMethod = NormalizeDeliveryMethod(request.DeliveryMethod);

        string? screenshotUrl = request.PaymentScreenshotUrl;
        if (!string.IsNullOrWhiteSpace(request.PaymentScreenshotBase64))
        {
            screenshotUrl = await ProcessScreenshotBase64Async(request.PaymentScreenshotBase64, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(screenshotUrl) && screenshotUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            screenshotUrl = await ProcessScreenshotBase64Async(screenshotUrl, cancellationToken);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            DeliveryMethod = deliveryMethod,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            CouponCode = request.CouponCode,
            Notes = request.Notes,
            PlacedAtUtc = DateTime.UtcNow,
            UtrNumber = request.UtrNumber,
            PaymentScreenshotUrl = screenshotUrl,
            PaymentSubmittedAtUtc = !string.IsNullOrWhiteSpace(request.UtrNumber) || !string.IsNullOrWhiteSpace(screenshotUrl) ? DateTime.UtcNow : null,
            ShippingAddress = request.ShippingAddress,
            BillingAddress = request.BillingAddress ?? (request.ShippingAddress with { })
            // NO TrackingNumber here. A tracking/LR number is a real document issued by the
            // transport company; it exists only once the parcel has physically been handed over.
            // It is set exactly once, by DispatchOrderAsync, from what the admin types in.
        };

        // Lines feed the shared calculator so the order bills exactly what /cart/calculate quoted.
        var pricedLines = new List<PricedLine>();
        var itemsSubtotal = Money.Zero();

        foreach (var reqItem in request.Items)
        {
            if (!products.TryGetValue(reqItem.ProductId, out var product))
                throw new ResourceNotFoundException(nameof(Product), reqItem.ProductId);

            if (product.AvailableQuantity < reqItem.Quantity)
            {
                throw new InsufficientStockException(product.SKU, product.AvailableQuantity, reqItem.Quantity);
            }

            // Reserve stock directly on Product (single source of truth)
            product.ReservedQuantity += reqItem.Quantity;

            var line = new PricedLine(product.Price, reqItem.Quantity, product.TaxRate);
            pricedLines.Add(line);

            var unitPrice = line.UnitPrice;
            var lineTotalBeforeTax = line.LineTotalBeforeTax;
            var itemTax = line.Tax;

            var primaryImg = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
                ?? product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

            // MRP snapshot for the printed estimate's "Rate/Qty (MRP)" and "Discount %" columns.
            // Frozen here so a later catalogue re-price can never rewrite a historical document, and
            // stored ONLY when the list price genuinely exceeds what was charged — a compare-at price
            // that is absent, equal to, or below the unit price is not a discount and stays null.
            var compareAtSnapshot = product.CompareAtPrice is { } mrp && mrp > unitPrice
                ? mrp
                : (Money?)null;

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                SKUSnapshot = product.SKU,
                ProductImageUrlSnapshot = primaryImg,
                UnitPrice = unitPrice,
                CostPriceSnapshot = product.CostPrice,
                CompareAtPriceSnapshot = compareAtSnapshot,
                Quantity = reqItem.Quantity,
                Discount = Money.Zero(),
                Tax = itemTax,
                LineTotal = lineTotalBeforeTax
            };

            order.Items.Add(orderItem);
            itemsSubtotal += lineTotalBeforeTax;
        }

        // Apply Promotion/Coupon if specified — resolved by the same gate POST /cart/calculate uses.
        var (promo, discount) = await _pricing.ResolveCouponAsync(request.CouponCode, itemsSubtotal, customer.Id, cancellationToken);
        if (promo != null)
        {
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

        // Every money component below comes from the shared calculator, the same one that answers
        // POST /cart/calculate. Delivery is NEVER charged (freight is settled with the transport
        // company on collection); packing charges are a real billed line at the rate held in the
        // SystemSettings key Order.PackingChargePercent (default 1.5%).
        var packingChargePercent = await _pricing.GetPackingChargePercentAsync(cancellationToken);
        var totals = _pricing.CalculateTotals(pricedLines, discount, packingChargePercent);

        order.ItemsSubtotal = totals.ItemsSubtotal;
        order.Tax = totals.Tax;
        order.Discount = totals.Discount;
        order.ShippingCharge = totals.ShippingCharge;
        order.PackingCharges = totals.PackingCharges;
        order.PackingChargePercent = totals.PackingChargePercent;
        order.GrandTotal = totals.GrandTotal;

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
        if (order.OrderStatus == request.NewStatus)
        {
            if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
            {
                order.TrackingNumber = request.TrackingNumber.Trim();
                await _context.SaveChangesAsync(cancellationToken);
            }
            return await GetOrderByIdAsync(order.Id, cancellationToken)
                ?? throw new InvalidOperationException("Failed to retrieve order");
        }

        order.ChangeStatus(request.NewStatus, request.Reason, _currentUser.UserName ?? "Admin");
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            order.TrackingNumber = request.TrackingNumber.Trim();
        }

        // Handle Cancellation -> Release reserved stock
        if (request.NewStatus == OrderStatus.Cancelled)
        {
            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null)
                {
                    prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
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
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var item in order.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null)
                {
                    prod.StockQuantity = Math.Max(0, prod.StockQuantity - item.Quantity);
                    prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
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

        // Reward points: earn floor(Total / 100) once, when the order is delivered
        if (request.NewStatus == OrderStatus.Delivered && !order.RewardPointsAwarded)
        {
            var earnedPoints = (int)Math.Floor(order.GrandTotal.ToDecimal() / 100m);
            if (earnedPoints > 0 && order.Customer != null)
            {
                order.Customer.RewardPoints += earnedPoints;
            }
            order.RewardPointsAwarded = true;
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

    public async Task<OrderDto> DispatchOrderAsync(Guid orderId, DispatchOrderRequest request, CancellationToken cancellationToken = default)
    {
        var carrierName = request.CarrierName?.Trim();
        var trackingNumber = request.TrackingNumber?.Trim();

        if (string.IsNullOrWhiteSpace(carrierName))
            throw new DomainException("Carrier name is required to dispatch an order.");

        if (string.IsNullOrWhiteSpace(trackingNumber))
            throw new DomainException("LR / waybill number is required to dispatch an order.");

        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistories)
            .Include(o => o.Invoices)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Order), orderId);

        if (order.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.Processing or OrderStatus.Packed))
        {
            throw new InvalidOrderStateTransitionException(order.OrderStatus.ToString(), OrderStatus.Shipped.ToString());
        }

        var operatorName = _currentUser.UserName ?? _currentUser.Email ?? "Dispatch Desk";
        var oldStatus = order.OrderStatus;

        order.CarrierName = carrierName;
        order.TrackingNumber = trackingNumber;

        var reason = $"Dispatched via {carrierName} — LR {trackingNumber}";
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            reason += $" | {request.Notes.Trim()}";
        }

        // ChangeStatus appends the status-history entry and syncs FulfillmentStatus to Shipped.
        order.ChangeStatus(OrderStatus.Shipped, reason, operatorName);
        order.FulfillmentStatus = FulfillmentStatus.Shipped;

        // Dispatch fulfils the reservation: convert reserved stock into a permanent deduction,
        // the same way UpdateOrderStatusAsync does when an order moves to Shipped.
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (prod != null)
            {
                prod.StockQuantity = Math.Max(0, prod.StockQuantity - item.Quantity);
                prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
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
            after: new { Status = order.OrderStatus.ToString(), order.CarrierName, order.TrackingNumber },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("OrderDispatched", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            order.CarrierName,
            order.TrackingNumber,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.OrderStatus.ToString(),
            CustomerEmail = order.Customer?.Email
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve dispatched order");
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

    public async Task<List<OrderDto>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return new List<OrderDto>();
        }

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId || c.Email == _currentUser.Email, cancellationToken);

        if (customer == null)
        {
            return new List<OrderDto>();
        }

        return await GetCustomerOrdersAsync(customer.Id, cancellationToken);
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

        var (etaMin, etaMax) = GetDeliveryEta(order.DeliveryMethod);

        return new OrderTrackingDto
        {
            OrderNumber = order.OrderNumber,
            Status = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            PaymentMethod = order.PaymentMethod,
            UtrNumber = order.UtrNumber,
            PaymentScreenshotUrl = order.PaymentScreenshotUrl,
            PlacedAtUtc = order.PlacedAtUtc,
            EstimatedDeliveryUtc = order.PlacedAtUtc.AddDays(etaMax),
            DeliveryMethod = NormalizeDeliveryMethod(order.DeliveryMethod),
            ExpectedDeliveryFrom = order.PlacedAtUtc.AddDays(etaMin),
            ExpectedDeliveryTo = order.PlacedAtUtc.AddDays(etaMax),
            CarrierName = order.CarrierName,
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
                CompareAtPrice = i.CompareAtPriceSnapshot?.ToDecimal(),
                Quantity = i.Quantity,
                Discount = i.Discount.ToDecimal(),
                Tax = i.Tax.ToDecimal(),
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList(),
            GrandTotal = Math.Max(0m, order.ItemsSubtotal.ToDecimal() - order.Discount.ToDecimal() + order.Tax.ToDecimal() + order.ShippingCharge.ToDecimal() + order.PackingCharges.ToDecimal())
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
        var rawScreenshot = request.PaymentScreenshotUrl ?? request.PaymentScreenshotBase64 ?? request.ScreenshotBase64;
        order.PaymentScreenshotUrl = await ProcessScreenshotBase64Async(rawScreenshot, cancellationToken);
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

        // Release reserved stock at product level
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (prod != null)
            {
                prod.ReservedQuantity = Math.Max(0, prod.ReservedQuantity - item.Quantity);
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

    // Owner's real Sivakasi logistics: goods travel by lorry and the customer settles the
    // freight directly with the transport company when collecting the parcel. The store
    // therefore has exactly ONE delivery option and NEVER charges for delivery (it is not
    // "free shipping" — no shipping is sold at all).
    //
    // The legacy storefront codes ("standard", "express", "godown-pickup", "parcel-service")
    // are still accepted on input and normalize to "transport", so in-flight clients and
    // orders already stored in the database keep working.
    private const string DeliveryMethodTransport = "transport";
    private const string TransportName = "Transport Delivery";

    private const string DefaultTransportNote = "Freight is payable directly to the transport company when you collect the parcel.";
    private const int DefaultEtaMinDays = 7;
    private const int DefaultEtaMaxDays = 14;

    private const string TransportNoteSettingKey = "Delivery.TransportNote";
    private const string EtaMinDaysSettingKey = "Delivery.EtaMinDays";
    private const string EtaMaxDaysSettingKey = "Delivery.EtaMaxDays";

    private sealed record DeliverySettings(string TransportNote, int EtaMinDays, int EtaMaxDays);

    /// <summary>
    /// Maps any accepted delivery code onto the single canonical code stored on the order.
    /// Every legacy alias (standard / express / godown-pickup / parcel-service) and anything
    /// unrecognised normalizes to "transport".
    /// </summary>
    private static string NormalizeDeliveryMethod(string? deliveryMethod) => DeliveryMethodTransport;

    private static (int EtaMinDays, int EtaMaxDays) GetDeliveryEta(string? deliveryMethod) =>
        (DefaultEtaMinDays, DefaultEtaMaxDays);

    private async Task<DeliverySettings> GetDeliverySettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _context.SystemSettings
            .AsNoTracking()
            .Where(s => (s.Key == TransportNoteSettingKey || s.Key == EtaMinDaysSettingKey || s.Key == EtaMaxDaysSettingKey) && !s.IsDeleted)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        int ParseInt(string key, int fallback) =>
            settings.TryGetValue(key, out var raw)
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= 0
                ? value
                : fallback;

        var note = settings.TryGetValue(TransportNoteSettingKey, out var rawNote) && !string.IsNullOrWhiteSpace(rawNote)
            ? rawNote.Trim()
            : DefaultTransportNote;

        return new DeliverySettings(
            note,
            ParseInt(EtaMinDaysSettingKey, DefaultEtaMinDays),
            ParseInt(EtaMaxDaysSettingKey, DefaultEtaMaxDays));
    }

    /// <summary>
    /// Exactly one option, always at zero charge. The <paramref name="subtotal"/> argument is
    /// retained for wire compatibility with existing clients but no longer affects the result
    /// (there is no free-shipping threshold any more — nothing is ever charged for delivery).
    /// </summary>
    public async Task<List<DeliveryOptionDto>> GetDeliveryOptionsAsync(decimal subtotal, CancellationToken cancellationToken = default)
    {
        var settings = await GetDeliverySettingsAsync(cancellationToken);

        return new List<DeliveryOptionDto>
        {
            new()
            {
                Code = DeliveryMethodTransport,
                Name = TransportName,
                Charge = 0m,
                EtaMinDays = settings.EtaMinDays,
                EtaMaxDays = settings.EtaMaxDays,
                Note = settings.TransportNote
            }
        };
    }

    // Packing-charge rate and arithmetic live in OrderPricingService — the single calculator
    // shared with POST /cart/calculate. Do not reintroduce a local copy here.

    private async Task<Customer?> ResolveCurrentCustomerAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var email = _currentUser.Email;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email)) return null;

        return await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                (userId != null && c.UserId == userId) ||
                (email != null && c.Email.ToLower() == email.ToLower()), cancellationToken);
    }

    private static OrderDto MapToOrderDto(Order o)
    {
        var (etaMin, etaMax) = GetDeliveryEta(o.DeliveryMethod);
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
            PackingCharges = o.PackingCharges.ToDecimal(),
            PackingChargePercent = o.PackingChargePercent,
            GrandTotal = o.ItemsSubtotal.ToDecimal() > 0
                ? Math.Max(0m, o.ItemsSubtotal.ToDecimal() - o.Discount.ToDecimal() + o.Tax.ToDecimal() + o.ShippingCharge.ToDecimal() + o.PackingCharges.ToDecimal())
                : o.GrandTotal.ToDecimal(),
            CouponCode = o.CouponCode,
            Notes = o.Notes,
            CarrierName = o.CarrierName,
            TrackingNumber = o.TrackingNumber,
            PlacedAtUtc = o.PlacedAtUtc,
            DeliveryMethod = NormalizeDeliveryMethod(o.DeliveryMethod),
            ExpectedDeliveryFrom = o.PlacedAtUtc.AddDays(etaMin),
            ExpectedDeliveryTo = o.PlacedAtUtc.AddDays(etaMax),
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
                CompareAtPrice = i.CompareAtPriceSnapshot?.ToDecimal(),
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
}
