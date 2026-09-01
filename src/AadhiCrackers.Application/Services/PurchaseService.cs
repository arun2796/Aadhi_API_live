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
    Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page = 1, int pageSize = 20, PurchaseOrderStatus? status = null, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> SubmitPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> ApprovePurchaseOrderAsync(Guid id, ApprovePurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> RejectPurchaseOrderAsync(Guid id, RejectPurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDto> CancelPurchaseOrderAsync(Guid id, CancelPurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default);
}

public class PurchaseService : IPurchaseService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IBusinessNumberGenerator _numberGenerator;

    public PurchaseService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog,
        IBusinessNumberGenerator numberGenerator)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _numberGenerator = numberGenerator;
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
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Suppliers",
            nameof(Supplier),
            supplier.Id.ToString(),
            supplier.Name,
            after: supplier,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

    public async Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page = 1, int pageSize = 20, PurchaseOrderStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .Include(p => p.Items)
            .Where(p => !p.IsDeleted);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

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
        var p = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);

        if (p == null) return null;

        return MapToPurchaseOrderDto(p);
    }

    public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var supplier = await _context.Suppliers.FindAsync(new object[] { request.SupplierId }, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Supplier), request.SupplierId);

        var warehouse = await _context.Warehouses.FindAsync(new object[] { request.WarehouseId }, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Warehouse), request.WarehouseId);

        if (request.Items.Count == 0)
            throw new DomainException("Purchase order must contain at least one item.");

        var poNumber = await _numberGenerator.GeneratePurchaseOrderNumberAsync(cancellationToken);

        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            SupplierId = supplier.Id,
            WarehouseId = warehouse.Id,
            Status = PurchaseOrderStatus.Draft,
            OrderDateUtc = DateTime.UtcNow,
            ExpectedDeliveryDateUtc = request.ExpectedDeliveryDateUtc,
            Notes = request.Notes
        };

        var subtotal = Money.Zero();

        foreach (var item in request.Items)
        {
            var product = await _context.Products.FindAsync(new object[] { item.ProductId }, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Product), item.ProductId);

            var unitPrice = Money.FromDecimal(item.UnitPrice);
            var lineTotal = unitPrice * item.Quantity;

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
        }

        po.Subtotal = subtotal;
        po.Tax = subtotal * 0.18m; // Standard 18% GST for crackers
        po.GrandTotal = po.Subtotal + po.Tax;

        _context.PurchaseOrders.Add(po);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.PurchaseCreated,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, GrandTotal = po.GrandTotal.ToDecimal(), po.Status },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetPurchaseOrderByIdAsync(po.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDto> SubmitPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), id);

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new DomainException($"Cannot submit purchase order with status '{po.Status}'. Only Draft orders can be submitted.");

        po.Status = PurchaseOrderStatus.Submitted;
        po.SubmittedAtUtc = DateTime.UtcNow;
        po.SubmittedBy = _currentUser.UserName ?? "User";
        po.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, po.Status, po.SubmittedBy, po.SubmittedAtUtc },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetPurchaseOrderByIdAsync(po.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDto> ApprovePurchaseOrderAsync(Guid id, ApprovePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), id);

        if (po.Status != PurchaseOrderStatus.Submitted && po.Status != PurchaseOrderStatus.Draft)
            throw new DomainException($"Cannot approve purchase order with status '{po.Status}'.");

        po.Status = PurchaseOrderStatus.Approved;
        po.ApprovedAtUtc = DateTime.UtcNow;
        po.ApprovedBy = _currentUser.UserName ?? "Admin";
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            po.Notes = string.IsNullOrWhiteSpace(po.Notes) ? request.Notes : $"{po.Notes}\nApproval Note: {request.Notes}";
        }
        po.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, po.Status, po.ApprovedBy, po.ApprovedAtUtc },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetPurchaseOrderByIdAsync(po.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDto> RejectPurchaseOrderAsync(Guid id, RejectPurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), id);

        if (po.Status != PurchaseOrderStatus.Submitted && po.Status != PurchaseOrderStatus.Draft)
            throw new DomainException($"Cannot reject purchase order with status '{po.Status}'.");

        po.Status = PurchaseOrderStatus.Rejected;
        po.RejectedAtUtc = DateTime.UtcNow;
        po.RejectedBy = _currentUser.UserName ?? "Admin";
        po.RejectionReason = request.Reason;
        po.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, po.Status, po.RejectedBy, po.RejectionReason },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetPurchaseOrderByIdAsync(po.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDto> CancelPurchaseOrderAsync(Guid id, CancelPurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), id);

        if (po.Status == PurchaseOrderStatus.Received || po.Status == PurchaseOrderStatus.Cancelled)
            throw new DomainException($"Cannot cancel purchase order with status '{po.Status}'.");

        po.Status = PurchaseOrderStatus.Cancelled;
        po.CancelledAtUtc = DateTime.UtcNow;
        po.CancelledBy = _currentUser.UserName ?? "Admin";
        po.CancellationReason = request.Reason;
        po.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Purchases",
            nameof(PurchaseOrder),
            po.Id.ToString(),
            po.PoNumber,
            after: new { po.PoNumber, po.Status, po.CancelledBy, po.CancellationReason },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetPurchaseOrderByIdAsync(po.Id, cancellationToken))!;
    }

    public async Task<GoodsReceiptDto> CreateGoodsReceiptAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .Include(p => p.Warehouse)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(PurchaseOrder), request.PurchaseOrderId);

        if (po.Status != PurchaseOrderStatus.Approved && po.Status != PurchaseOrderStatus.PartiallyReceived)
            throw new DomainException($"Cannot receive goods for purchase order with status '{po.Status}'. PO must be Approved first.");

        var grnNumber = await _numberGenerator.GenerateGoodsReceiptNumberAsync(cancellationToken);

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
            var acceptedQty = itemReq.QuantityAccepted > 0
                ? itemReq.QuantityAccepted
                : Math.Max(0, itemReq.QuantityReceived - itemReq.QuantityRejected - itemReq.QuantityDamaged);

            var lineTotal = unitPrice * acceptedQty;

            grn.Items.Add(new GoodsReceiptItem
            {
                GoodsReceiptId = grn.Id,
                PurchaseOrderItemId = poItem.Id,
                ProductId = product.Id,
                QuantityOrdered = poItem.QuantityOrdered,
                QuantityReceived = itemReq.QuantityReceived,
                QuantityAccepted = acceptedQty,
                QuantityRejected = itemReq.QuantityRejected,
                QuantityDamaged = itemReq.QuantityDamaged,
                RejectionReason = itemReq.RejectionReason,
                UnitPrice = unitPrice,
                LineTotal = lineTotal
            });

            // Update PO Item quantity received (accepted + damaged)
            poItem.QuantityReceived += (acceptedQty + itemReq.QuantityDamaged);

            // Add ONLY accepted sellable inventory to warehouse StockItem
            if (acceptedQty > 0)
            {
                var stockItem = await _context.StockItems
                    .FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == po.WarehouseId, cancellationToken);
                var beforeOnHand = stockItem?.QuantityOnHand ?? 0;
                if (stockItem != null)
                {
                    stockItem.QuantityOnHand += acceptedQty;
                }
                else
                {
                    stockItem = new StockItem
                    {
                        ProductId = product.Id,
                        WarehouseId = po.WarehouseId,
                        QuantityOnHand = acceptedQty,
                        QuantityReserved = 0,
                        ReorderLevel = product.ReorderLevel
                    };
                    _context.StockItems.Add(stockItem);
                }

                product.StockQuantity = stockItem.QuantityOnHand;
                product.ReservedQuantity = stockItem.QuantityReserved;

                // Ledger record for sellable stock increase
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = po.WarehouseId,
                    MovementType = StockMovementType.Purchase,
                    QuantityChange = acceptedQty,
                    QuantityBefore = beforeOnHand,
                    QuantityAfter = stockItem.QuantityOnHand,
                    ReferenceType = "GoodsReceipt",
                    ReferenceId = grn.ReceiptNumber,
                    Reason = $"Received sellable goods for PO #{po.PoNumber} via GRN #{grn.ReceiptNumber}",
                    CreatedBy = _currentUser.UserName ?? "Admin",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            // If any items are damaged, record damage ledger entry without changing on-hand sellable inventory
            if (itemReq.QuantityDamaged > 0)
            {
                var currentStock = await _context.StockItems.FirstOrDefaultAsync(s => s.ProductId == product.Id && s.WarehouseId == po.WarehouseId, cancellationToken);
                var onHand = currentStock?.QuantityOnHand ?? product.StockQuantity;

                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = po.WarehouseId,
                    MovementType = StockMovementType.Damage,
                    QuantityChange = 0,
                    QuantityBefore = onHand,
                    QuantityAfter = onHand,
                    ReferenceType = "GoodsReceiptDamage",
                    ReferenceId = grn.ReceiptNumber,
                    Reason = $"Damaged goods ({itemReq.QuantityDamaged} units) received in GRN #{grn.ReceiptNumber}. Reason: {itemReq.RejectionReason ?? "Transit damage"}",
                    CreatedBy = _currentUser.UserName ?? "Admin",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        // Check if all items fully received
        var allReceived = po.Items.All(i => i.QuantityReceived >= i.QuantityOrdered);
        po.Status = allReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;

        _context.GoodsReceipts.Add(grn);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.GoodsReceived,
            "Purchases",
            nameof(GoodsReceipt),
            grn.Id.ToString(),
            grn.ReceiptNumber,
            after: new { grn.ReceiptNumber, po.PoNumber, po.Status },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
                QuantityOrdered = i.QuantityOrdered,
                QuantityReceived = i.QuantityReceived,
                QuantityAccepted = i.QuantityAccepted,
                QuantityRejected = i.QuantityRejected,
                QuantityDamaged = i.QuantityDamaged,
                RejectionReason = i.RejectionReason,
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
            SubmittedAtUtc = p.SubmittedAtUtc,
            SubmittedBy = p.SubmittedBy,
            ApprovedAtUtc = p.ApprovedAtUtc,
            ApprovedBy = p.ApprovedBy,
            RejectedAtUtc = p.RejectedAtUtc,
            RejectedBy = p.RejectedBy,
            RejectionReason = p.RejectionReason,
            CancelledAtUtc = p.CancelledAtUtc,
            CancelledBy = p.CancelledBy,
            CancellationReason = p.CancellationReason,
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
