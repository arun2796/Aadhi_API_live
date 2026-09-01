using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Contracts.Quotes;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IQuoteService
{
    Task<PagedResult<QuoteDto>> GetQuotesAsync(int page = 1, int pageSize = 20, QuoteStatus? status = null, CancellationToken cancellationToken = default);
    Task<QuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QuoteDto> CreateQuoteAsync(CreateQuoteRequest request, CancellationToken cancellationToken = default);
    Task<QuoteDto> UpdateQuoteStatusAsync(Guid id, UpdateQuoteStatusRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> ConvertQuoteToOrderAsync(Guid id, CancellationToken cancellationToken = default);
}

public class QuoteService : IQuoteService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IBusinessNumberGenerator _numberGenerator;
    private readonly IOutboxService _outbox;
    private readonly IOrderService _orderService;

    public QuoteService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IBusinessNumberGenerator numberGenerator,
        IOutboxService outbox,
        IOrderService orderService)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _numberGenerator = numberGenerator;
        _outbox = outbox;
        _orderService = orderService;
    }

    public async Task<PagedResult<QuoteDto>> GetQuotesAsync(int page = 1, int pageSize = 20, QuoteStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Quotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Items)
                .ThenInclude(i => i.Product)
            .Where(q => !q.IsDeleted);

        if (status.HasValue)
        {
            query = query.Where(q => q.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var quotes = await query
            .OrderByDescending(q => q.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = quotes.Select(MapToDto).ToList();
        return new PagedResult<QuoteDto>(items, totalCount, page, pageSize);
    }

    public async Task<QuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await _context.Quotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

        return quote == null ? null : MapToDto(quote);
    }

    public async Task<QuoteDto> CreateQuoteAsync(CreateQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new DomainException("Quote must contain at least one line item.");
        }

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && !c.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Customer), request.CustomerId);

        var quoteNumber = await _numberGenerator.GenerateQuoteNumberAsync(cancellationToken);
        var quote = new Quote
        {
            QuoteNumber = quoteNumber,
            CustomerId = customer.Id,
            Customer = customer,
            ExpiryDateUtc = request.ExpiryDateUtc ?? DateTime.UtcNow.AddDays(30),
            Notes = request.Notes,
            Status = QuoteStatus.Draft,
            Discount = Money.FromDecimal(Math.Max(0, request.DiscountAmount))
        };

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        decimal subtotal = 0;
        decimal taxTotal = 0;

        foreach (var itemReq in request.Items)
        {
            if (!products.TryGetValue(itemReq.ProductId, out var product))
            {
                throw new ResourceNotFoundException(nameof(Product), itemReq.ProductId);
            }

            if (itemReq.Quantity <= 0)
            {
                throw new DomainException($"Quantity for product '{product.Name}' must be greater than zero.");
            }

            var unitPrice = itemReq.CustomUnitPrice.HasValue && itemReq.CustomUnitPrice.Value > 0
                ? Money.FromDecimal(itemReq.CustomUnitPrice.Value)
                : product.Price;

            var quoteItem = new QuoteItem
            {
                ProductId = product.Id,
                Product = product,
                Quantity = itemReq.Quantity,
                UnitPrice = unitPrice,
                DiscountPercentage = Math.Clamp(itemReq.DiscountPercentage, 0, 100)
            };

            quoteItem.CalculateLineTotal();
            quote.Items.Add(quoteItem);

            subtotal += quoteItem.LineTotal.ToDecimal();
            taxTotal += quoteItem.LineTotal.ToDecimal() * (product.TaxRate / 100m);
        }

        quote.Subtotal = Money.FromDecimal(subtotal);
        quote.Tax = Money.FromDecimal(taxTotal);
        quote.GrandTotal = Money.FromDecimal(Math.Max(0, subtotal - quote.Discount.ToDecimal() + taxTotal));

        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.QuoteCreated,
            "Quotes",
            nameof(Quote),
            quote.Id.ToString(),
            quote.QuoteNumber,
            after: new { quote.QuoteNumber, GrandTotal = quote.GrandTotal.ToDecimal(), quote.Status },
            cancellationToken: cancellationToken);

        return MapToDto(quote);
    }

    public async Task<QuoteDto> UpdateQuoteStatusAsync(Guid id, UpdateQuoteStatusRequest request, CancellationToken cancellationToken = default)
    {
        var quote = await _context.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Quote), id);

        if (quote.Status == QuoteStatus.Converted)
        {
            throw new DomainException("Cannot modify status of an already converted quote.");
        }

        var oldStatus = quote.Status;
        quote.Status = request.Status;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            quote.Notes = request.Notes;
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.QuoteStatusChanged,
            "Quotes",
            nameof(Quote),
            quote.Id.ToString(),
            quote.QuoteNumber,
            before: new { Status = oldStatus.ToString() },
            after: new { Status = quote.Status.ToString() },
            cancellationToken: cancellationToken);

        return MapToDto(quote);
    }

    public async Task<OrderDto> ConvertQuoteToOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await _context.Quotes
            .Include(q => q.Customer)
                .ThenInclude(c => c!.Addresses)
            .Include(q => q.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Quote), id);

        if (quote.Status == QuoteStatus.Converted)
        {
            throw new DomainException($"Quote '{quote.QuoteNumber}' has already been converted to an Order.");
        }

        if (quote.Status == QuoteStatus.Rejected)
        {
            throw new DomainException($"Quote '{quote.QuoteNumber}' is rejected and cannot be converted.");
        }

        if (quote.ExpiryDateUtc < DateTime.UtcNow)
        {
            quote.Status = QuoteStatus.Expired;
            await _context.SaveChangesAsync(cancellationToken);
            throw new DomainException($"Quote '{quote.QuoteNumber}' expired on {quote.ExpiryDateUtc:yyyy-MM-dd} and cannot be converted.");
        }

        if (quote.Items.Count == 0)
        {
            throw new DomainException($"Quote '{quote.QuoteNumber}' contains no line items to convert.");
        }

        var customer = quote.Customer
            ?? throw new DomainException($"Customer record for quote '{quote.QuoteNumber}' is missing.");

        var defaultAddress = customer.Addresses.FirstOrDefault(a => a.IsDefault)
            ?? customer.Addresses.FirstOrDefault();

        Address CreateShippingAddress() => defaultAddress != null && defaultAddress.Address != null
            ? new Address(
                defaultAddress.Address.FullName,
                defaultAddress.Address.Phone,
                defaultAddress.Address.AddressLine1,
                defaultAddress.Address.AddressLine2,
                defaultAddress.Address.City,
                defaultAddress.Address.State,
                defaultAddress.Address.PostalCode,
                defaultAddress.Address.Country)
            : new Address(
                $"{customer.FirstName} {customer.LastName}".Trim(),
                customer.Phone,
                "Wholesale Delivery Address",
                null,
                "Sivakasi",
                "Tamil Nadu",
                "626123",
                "India");

        // Validate and reserve stock across all quote items atomically
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var primaryWarehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.IsActive && !w.IsDeleted, cancellationToken)
            ?? throw new DomainException("No active warehouse found for stock reservation.");

        var orderNumber = await _numberGenerator.GenerateOrderNumberAsync(cancellationToken);
        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            Customer = customer,
            WarehouseId = primaryWarehouse.Id,
            Warehouse = primaryWarehouse,
            OrderStatus = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.BankTransfer,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            ShippingAddress = CreateShippingAddress(),
            BillingAddress = CreateShippingAddress(),
            Notes = $"Converted from Wholesale Quote #{quote.QuoteNumber}. {quote.Notes}".Trim()
        };

        decimal itemsSubtotal = 0;
        decimal totalTax = 0;

        foreach (var item in quote.Items)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == item.ProductId && !p.IsDeleted, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Product), item.ProductId);

            var stockItem = await _context.StockItems
                .FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == primaryWarehouse.Id, cancellationToken);

            if (stockItem == null)
            {
                stockItem = new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = primaryWarehouse.Id,
                    QuantityOnHand = product.StockQuantity,
                    QuantityReserved = 0
                };
                _context.StockItems.Add(stockItem);
            }

            if (stockItem.QuantityAvailable < item.Quantity)
            {
                throw new DomainException($"Insufficient stock for '{product.Name}'. Available: {stockItem.QuantityAvailable}, Required: {item.Quantity}");
            }

            // Reserve stock
            var beforeReserved = stockItem.QuantityReserved;
            stockItem.QuantityReserved += item.Quantity;
            product.ReservedQuantity += item.Quantity;

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                WarehouseId = primaryWarehouse.Id,
                MovementType = StockMovementType.StockReserved,
                QuantityChange = item.Quantity,
                QuantityBefore = beforeReserved,
                QuantityAfter = stockItem.QuantityReserved,
                ReferenceType = "QuoteConversionOrder",
                ReferenceId = order.Id.ToString(),
                Reason = $"Stock reserved for Quote #{quote.QuoteNumber} converted to Order #{order.OrderNumber}"
            });

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Product = product,
                ProductNameSnapshot = product.Name,
                SKUSnapshot = product.SKU,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Discount = Money.FromDecimal(item.UnitPrice.ToDecimal() * item.Quantity * (item.DiscountPercentage / 100m)),
                Tax = Money.FromDecimal(item.LineTotal.ToDecimal() * (product.TaxRate / 100m)),
                LineTotal = item.LineTotal,
                CostPriceSnapshot = product.CostPrice
            };

            order.Items.Add(orderItem);
            itemsSubtotal += orderItem.LineTotal.ToDecimal();
            totalTax += orderItem.Tax.ToDecimal();
        }

        order.ItemsSubtotal = Money.FromDecimal(itemsSubtotal);
        order.Discount = quote.Discount;
        order.Tax = Money.FromDecimal(totalTax);
        order.ShippingCharge = Money.Zero(); // Wholesale orders include shipping or free
        order.GrandTotal = Money.FromDecimal(Math.Max(0, itemsSubtotal - quote.Discount.ToDecimal() + totalTax));

        order.StatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Confirmed,
            Reason = $"Created from Quote #{quote.QuoteNumber}",
            ChangedBy = _currentUser.UserName ?? "Admin",
            ChangedAtUtc = DateTime.UtcNow
        });

        // Automatically issue Tax Invoice
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
            PaidAmount = Money.Zero(),
            BalanceAmount = order.GrandTotal,
            Status = InvoiceStatus.Issued,
            IssuedAtUtc = DateTime.UtcNow,
            DueDateUtc = DateTime.UtcNow.AddDays(15)
        };
        order.Invoices.Add(invoice);

        _context.Orders.Add(order);

        // Update Quote status to Converted
        quote.Status = QuoteStatus.Converted;
        quote.ConvertedOrderId = order.Id;

        await _auditLog.LogAsync(
            AuditAction.QuoteConverted,
            "Quotes",
            nameof(Quote),
            quote.Id.ToString(),
            quote.QuoteNumber,
            after: new { quote.QuoteNumber, OrderId = order.Id, order.OrderNumber },
            cancellationToken: cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.OrderCreated,
            "Orders",
            nameof(Order),
            order.Id.ToString(),
            order.OrderNumber,
            after: new { order.OrderNumber, GrandTotal = order.GrandTotal.ToDecimal(), order.OrderStatus },
            cancellationToken: cancellationToken);

        await _outbox.EnqueueAsync("OrderPlaced", new
        {
            OrderId = order.Id,
            order.OrderNumber,
            CustomerId = customer.Id,
            CustomerEmail = customer.Email,
            CustomerPhone = customer.Phone,
            GrandTotal = order.GrandTotal.ToDecimal(),
            Source = "QuoteConversion",
            QuoteNumber = quote.QuoteNumber
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await _orderService.GetOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve converted order");
    }

    private static QuoteDto MapToDto(Quote q)
    {
        return new QuoteDto
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            CustomerId = q.CustomerId,
            CustomerName = q.Customer != null ? $"{q.Customer.FirstName} {q.Customer.LastName}".Trim() : string.Empty,
            CustomerPhone = q.Customer?.Phone ?? string.Empty,
            CustomerEmail = q.Customer?.Email ?? string.Empty,
            Subtotal = q.Subtotal.ToDecimal(),
            Discount = q.Discount.ToDecimal(),
            Tax = q.Tax.ToDecimal(),
            GrandTotal = q.GrandTotal.ToDecimal(),
            Status = q.Status.ToString(),
            ExpiryDateUtc = q.ExpiryDateUtc,
            Notes = q.Notes,
            ConvertedOrderId = q.ConvertedOrderId,
            CreatedAtUtc = q.CreatedAtUtc,
            Items = q.Items.Select(i => new QuoteItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? string.Empty,
                Sku = i.Product?.SKU ?? string.Empty,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice.ToDecimal(),
                DiscountPercentage = i.DiscountPercentage,
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList()
        };
    }
}
