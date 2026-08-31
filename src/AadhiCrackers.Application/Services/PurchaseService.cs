using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IPurchaseService
{
    Task<List<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default);
    Task<SupplierDto> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default);
}

public class PurchaseService : IPurchaseService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;

    public PurchaseService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
    }

    public async Task<List<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .AsNoTracking()
            .Include(s => s.PurchaseOrders)
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                ContactPerson = s.ContactPerson,
                Email = s.Email,
                Phone = s.Phone,
                Address = s.Address,
                GstNumber = s.GstNumber,
                IsActive = s.IsActive,
                TotalPurchaseOrders = s.PurchaseOrders.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierDto> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken cancellationToken = default)
    {
        var count = await _context.Suppliers.CountAsync(cancellationToken) + 1;
        var supplier = new Supplier
        {
            Code = $"SUP-{count:D3}",
            Name = request.Name.Trim(),
            ContactPerson = request.ContactPerson,
            Email = request.Email,
            Phone = request.Phone.Trim(),
            Address = request.Address,
            GstNumber = request.GstNumber,
            IsActive = request.IsActive
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Suppliers",
            nameof(Supplier),
            supplier.Id.ToString(),
            supplier.Name,
            after: supplier,
            cancellationToken: cancellationToken);

        return new SupplierDto
        {
            Id = supplier.Id,
            Code = supplier.Code,
            Name = supplier.Name,
            ContactPerson = supplier.ContactPerson,
            Email = supplier.Email,
            Phone = supplier.Phone,
            Address = supplier.Address,
            GstNumber = supplier.GstNumber,
            IsActive = supplier.IsActive,
            TotalPurchaseOrders = 0
        };
    }

    public async Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items)
            .Where(p => !p.IsDeleted);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.OrderDateUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapToPurchaseOrderDto(p))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseOrderDto>(items, totalCount, page, pageSize);
    }

    public async Task<PurchaseOrderDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        return po == null ? null : MapToPurchaseOrderDto(po);
    }

    public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            throw new DomainException("Purchase Order must contain at least one item.");

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && !s.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Supplier), request.SupplierId);

        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId && !w.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Warehouse), request.WarehouseId);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var count = await _context.PurchaseOrders.CountAsync(cancellationToken) + 1;
        var poNumber = $"PO-{DateTime.UtcNow:yyyy}-{count:D6}";

        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            SupplierId = supplier.Id,
            WarehouseId = warehouse.Id,
            Status = PurchaseOrderStatus.Submitted,
            OrderDateUtc = DateTime.UtcNow,
            ExpectedDeliveryDateUtc = request.ExpectedDeliveryDateUtc ?? DateTime.UtcNow.AddDays(14),
            Notes = request.Notes
        };

        var subtotal = Money.Zero();
        var tax = Money.Zero();

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                throw new ResourceNotFoundException(nameof(Product), item.ProductId);

            var unitPrice = Money.FromDecimal(item.UnitPrice);
            var lineTotal = unitPrice * item.Quantity;
            var lineTax = lineTotal * (product.TaxRate / 100m);

            po.Items.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = po.Id,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                SKUSnapshot = product.SKU,
                UnitPrice = unitPrice,
                QuantityOrdered = item.Quantity,
                QuantityReceived = 0,
                LineTotal = lineTotal
            });

            subtotal += lineTotal;
            tax += lineTax;
        }

        po.Subtotal = subtotal;
        po.Tax = tax;
        po.GrandTotal = subtotal + tax;

        _context.PurchaseOrders.Add(po);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PurchaseCreated,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, GrandTotal = po.GrandTotal.ToDecimal(), po.Status },
            cancellationToken: cancellationToken);

        return await GetPurchaseOrderByIdAsync(po.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created purchase order");
    }

    public async Task<GoodsReceiptDto> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), request.PurchaseOrderId);

        var count = await _context.GoodsReceipts.CountAsync(cancellationToken) + 1;
        var grnNumber = $"GRN-{DateTime.UtcNow:yyyy}-{count:D6}";

        var grn = new GoodsReceipt
        {
            ReceiptNumber = grnNumber,
            PurchaseOrderId = po.Id,
            WarehouseId = po.WarehouseId,
            SupplierId = po.SupplierId,
            ReceivedDateUtc = DateTime.UtcNow,
            Notes = request.Notes
        };

        foreach (var itemReq in request.Items)
        {
            var poItem = po.Items.FirstOrDefault(i => i.Id == itemReq.PurchaseOrderItemId)
                ?? throw new ResourceNotFoundException(nameof(PurchaseOrderItem), itemReq.PurchaseOrderItemId);

            var product = await _context.Products.FindAsync(new object[] { itemReq.ProductId }, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Product), itemReq.ProductId);

            var unitPrice = Money.FromDecimal(itemReq.UnitPrice);
            var lineTotal = unitPrice * itemReq.QuantityReceived;

            grn.Items.Add(new GoodsReceiptItem
            {
                GoodsReceiptId = grn.Id,
                PurchaseOrderItemId = poItem.Id,
                ProductId = product.Id,
                QuantityReceived = itemReq.QuantityReceived,
                UnitPrice = unitPrice,
                LineTotal = lineTotal
            });

            // Update PO Item quantity received
            poItem.QuantityReceived += itemReq.QuantityReceived;

            // Increment Stock on Hand
            var before = product.StockQuantity;
            product.StockQuantity += itemReq.QuantityReceived;

            var stockItem = await _context.StockItems
                .FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == po.WarehouseId, cancellationToken);
            if (stockItem != null)
            {
                stockItem.QuantityOnHand += itemReq.QuantityReceived;
            }
            else
            {
                _context.StockItems.Add(new StockItem
                {
                    ProductId = product.Id,
                    WarehouseId = po.WarehouseId,
                    QuantityOnHand = itemReq.QuantityReceived,
                    QuantityReserved = 0,
                    ReorderLevel = product.ReorderLevel
                });
            }

            // Ledger record
            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                WarehouseId = po.WarehouseId,
                MovementType = StockMovementType.Purchase,
                QuantityChange = itemReq.QuantityReceived,
                QuantityBefore = before,
                QuantityAfter = product.StockQuantity,
                ReferenceType = "GoodsReceipt",
                ReferenceId = grn.ReceiptNumber,
                Reason = $"Received goods for PO #{po.PoNumber} via GRN #{grn.ReceiptNumber}",
                CreatedBy = _currentUser.UserName ?? "Admin",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Check if all items fully received
        var allReceived = po.Items.All(i => i.QuantityReceived >= i.QuantityOrdered);
        po.Status = allReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;

        _context.GoodsReceipts.Add(grn);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.GoodsReceived,
            "Purchases",
            nameof(GoodsReceipt),
            grn.Id.ToString(),
            grn.ReceiptNumber,
            after: new { grn.ReceiptNumber, po.PoNumber, po.Status },
            cancellationToken: cancellationToken);

        return new GoodsReceiptDto
        {
            Id = grn.Id,
            ReceiptNumber = grn.ReceiptNumber,
            PurchaseOrderId = po.Id,
            PoNumber = po.PoNumber,
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier.Name,
            WarehouseId = po.WarehouseId,
            WarehouseName = po.Warehouse.Name,
            ReceivedDateUtc = grn.ReceivedDateUtc,
            Notes = grn.Notes,
            Items = grn.Items.Select(i => new GoodsReceiptItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = (po.Items.FirstOrDefault(pi => pi.ProductId == i.ProductId)?.ProductNameSnapshot) ?? "Product",
                QuantityReceived = i.QuantityReceived,
                UnitPrice = i.UnitPrice.ToDecimal(),
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList()
        };
    }

    private static PurchaseOrderDto MapToPurchaseOrderDto(PurchaseOrder p)
    {
        return new PurchaseOrderDto
        {
            Id = p.Id,
            PoNumber = p.PoNumber,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.Name ?? "Supplier",
            WarehouseId = p.WarehouseId,
            WarehouseName = p.Warehouse?.Name ?? "Warehouse",
            Status = p.Status,
            Subtotal = p.Subtotal.ToDecimal(),
            Tax = p.Tax.ToDecimal(),
            GrandTotal = p.GrandTotal.ToDecimal(),
            OrderDateUtc = p.OrderDateUtc,
            ExpectedDeliveryDateUtc = p.ExpectedDeliveryDateUtc,
            Notes = p.Notes,
            Items = p.Items.Select(i => new PurchaseOrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                SKU = i.SKUSnapshot,
                UnitPrice = i.UnitPrice.ToDecimal(),
                QuantityOrdered = i.QuantityOrdered,
                QuantityReceived = i.QuantityReceived,
                LineTotal = i.LineTotal.ToDecimal()
            }).ToList()
        };
    }
}
