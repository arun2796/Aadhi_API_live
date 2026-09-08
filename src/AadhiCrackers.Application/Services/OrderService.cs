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

    public OrderService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IOutboxService outbox,
        IBusinessNumberGenerator numberGenerator,
        IFileStorageService? fileStorage = null)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _outbox = outbox;
        _numberGenerator = numberGenerator;
        _fileStorage = fileStorage;
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
            BillingAddress = request.BillingAddress ?? (request.ShippingAddress with { }),
            TrackingNumber = $"TRK-{Random.Shared.Next(10000000, 99999999)}"
        };

        var itemsSubtotal = Money.Zero();
        var totalTax = Money.Zero();

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

        // Shipping calculation: Sivakasi Cracker orders have NO online delivery charges (Transport freight is collected To-Pay at lorry office)
        var shippingCharge = Money.Zero();
        order.ShippingCharge = shippingCharge;

        order.GrandTotal = itemsSubtotal - discount + totalTax;

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
            DeliveryMethod = order.DeliveryMethod,
            ExpectedDeliveryFrom = order.PlacedAtUtc.AddDays(etaMin),
            ExpectedDeliveryTo = order.PlacedAtUtc.AddDays(etaMax),
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
            GrandTotal = Math.Max(0m, order.ItemsSubtotal.ToDecimal() - order.Discount.ToDecimal() + order.Tax.ToDecimal())
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

    private const string DeliveryMethodStandard = "standard";
    private const string DeliveryMethodExpress = "express";

    private sealed record DeliverySettings(decimal StandardCharge, decimal ExpressCharge, decimal FreeShippingThreshold);

    private static string NormalizeDeliveryMethod(string? deliveryMethod) =>
        string.Equals(deliveryMethod?.Trim(), DeliveryMethodExpress, StringComparison.OrdinalIgnoreCase)
            ? DeliveryMethodExpress
            : DeliveryMethodStandard;

    private static (int EtaMinDays, int EtaMaxDays) GetDeliveryEta(string? deliveryMethod) =>
        string.Equals(deliveryMethod?.Trim(), DeliveryMethodExpress, StringComparison.OrdinalIgnoreCase) ? (1, 2) : (3, 5);

    private async Task<DeliverySettings> GetDeliverySettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _context.SystemSettings
            .AsNoTracking()
            .Where(s => (s.Key == "Delivery.StandardCharge" || s.Key == "Delivery.ExpressCharge" || s.Key == "Shipping.FreeShippingThreshold") && !s.IsDeleted)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        decimal Parse(string key, decimal fallback) =>
            settings.TryGetValue(key, out var raw)
            && decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        return new DeliverySettings(
            Parse("Delivery.StandardCharge", 0m),
            Parse("Delivery.ExpressCharge", 90m),
            Parse("Shipping.FreeShippingThreshold", 3000m));
    }

    public async Task<List<DeliveryOptionDto>> GetDeliveryOptionsAsync(decimal subtotal, CancellationToken cancellationToken = default)
    {
        var settings = await GetDeliverySettingsAsync(cancellationToken);
        var standardCharge = subtotal >= settings.FreeShippingThreshold ? 0m : settings.StandardCharge;

        return new List<DeliveryOptionDto>
        {
            new()
            {
                Code = DeliveryMethodStandard,
                Name = "Standard Delivery (3-5 Days)",
                Charge = standardCharge,
                EtaMinDays = 3,
                EtaMaxDays = 5
            },
            new()
            {
                Code = DeliveryMethodExpress,
                Name = "Express Delivery (1-2 Days)",
                Charge = settings.ExpressCharge,
                EtaMinDays = 1,
                EtaMaxDays = 2
            }
        };
    }

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
            ShippingCharge = 0m,
            GrandTotal = o.ItemsSubtotal.ToDecimal() > 0
                ? Math.Max(0m, o.ItemsSubtotal.ToDecimal() - o.Discount.ToDecimal() + o.Tax.ToDecimal())
                : o.GrandTotal.ToDecimal(),
            CouponCode = o.CouponCode,
            Notes = o.Notes,
            TrackingNumber = o.TrackingNumber,
            PlacedAtUtc = o.PlacedAtUtc,
            DeliveryMethod = o.DeliveryMethod,
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
