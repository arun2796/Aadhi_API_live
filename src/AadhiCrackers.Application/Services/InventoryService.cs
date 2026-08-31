using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IInventoryService
{
    Task<List<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<StockItemDto>> GetStockItemsAsync(Guid? warehouseId = null, string? search = null, bool lowStockOnly = false, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<StockItemDto> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<bool> TransferStockAsync(StockTransferRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<StockMovementDto>> GetStockMovementsAsync(Guid? productId = null, Guid? warehouseId = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<List<LowStockAlertDto>> GetLowStockAlertsAsync(int limit = 10, CancellationToken cancellationToken = default);
}

public class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;

    public InventoryService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
    }

    public async Task<List<WarehouseDto>> GetWarehousesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .Include(w => w.StockItems)
            .Where(w => !w.IsDeleted)
            .OrderByDescending(w => w.IsPrimary)
            .ThenBy(w => w.Name)
            .Select(w => new WarehouseDto
            {
                Id = w.Id,
                Code = w.Code,
                Name = w.Name,
                Address = w.Address,
                Phone = w.Phone,
                IsActive = w.IsActive,
                IsPrimary = w.IsPrimary,
                TotalProducts = w.StockItems.Count,
                TotalStock = w.StockItems.Sum(s => s.QuantityOnHand)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<StockItemDto>> GetStockItemsAsync(Guid? warehouseId = null, string? search = null, bool lowStockOnly = false, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = _context.StockItems
            .AsNoTracking()
            .Include(s => s.Product).ThenInclude(p => p.Images)
            .Include(s => s.Warehouse)
            .Where(s => !s.Product.IsDeleted && !s.Warehouse.IsDeleted);

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(item => item.Product.Name.ToLower().Contains(s) || item.Product.SKU.ToLower().Contains(s));
        }

        if (lowStockOnly)
            query = query.Where(item => (item.QuantityOnHand - item.QuantityReserved) <= item.ReorderLevel);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => (s.QuantityOnHand - s.QuantityReserved))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StockItemDto
            {
                Id = s.Id,
                ProductId = s.ProductId,
                ProductName = s.Product.Name,
                SKU = s.Product.SKU,
                ImageUrl = s.Product.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary) != null
                    ? s.Product.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)!.Url
                    : s.Product.Images.OrderBy(i => i.SortOrder).FirstOrDefault() != null ? s.Product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()!.Url : null,
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                QuantityOnHand = s.QuantityOnHand,
                QuantityReserved = s.QuantityReserved,
                QuantityAvailable = Math.Max(0, s.QuantityOnHand - s.QuantityReserved),
                ReorderLevel = s.ReorderLevel
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StockItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<StockItemDto> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainException("An explicit reason is required for every stock adjustment.");

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Product), request.ProductId);

        var stockItem = await _context.StockItems
            .FirstOrDefaultAsync(s => s.ProductId == request.ProductId && s.WarehouseId == request.WarehouseId, cancellationToken);

        if (stockItem == null)
        {
            stockItem = new StockItem
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                QuantityOnHand = 0,
                QuantityReserved = 0,
                ReorderLevel = product.ReorderLevel
            };
            _context.StockItems.Add(stockItem);
        }

        var beforeQuantity = stockItem.QuantityOnHand;
        int newQuantity;
        int delta;

        if (request.IsRelative)
        {
            delta = request.AdjustedQuantity;
            newQuantity = beforeQuantity + delta;
        }
        else
        {
            newQuantity = request.AdjustedQuantity;
            delta = newQuantity - beforeQuantity;
        }

        if (newQuantity < 0)
            throw new DomainException($"Cannot adjust stock below zero. Current: {beforeQuantity}, Adjustment: {delta}");

        stockItem.QuantityOnHand = newQuantity;
        product.StockQuantity = Math.Max(0, product.StockQuantity + delta);

        // Immutable Ledger Entry
        var movement = new StockMovement
        {
            ProductId = product.Id,
            WarehouseId = request.WarehouseId,
            MovementType = StockMovementType.Adjustment,
            QuantityChange = delta,
            QuantityBefore = beforeQuantity,
            QuantityAfter = newQuantity,
            ReferenceType = "ManualAdjustment",
            Reason = request.Reason.Trim(),
            CreatedBy = _currentUser.UserName ?? "Admin",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.StockMovements.Add(movement);

        await _context.SaveChangesAsync(cancellationToken);

        // Enterprise Audit Logging
        await _auditLog.LogAsync(
            AuditAction.StockAdjusted,
            "Inventory",
            nameof(StockItem),
            stockItem.Id.ToString(),
            product.Name,
            before: new { QuantityOnHand = beforeQuantity },
            after: new { QuantityOnHand = newQuantity, Delta = delta, request.Reason },
            cancellationToken: cancellationToken);

        return new StockItemDto
        {
            Id = stockItem.Id,
            ProductId = stockItem.ProductId,
            ProductName = product.Name,
            SKU = product.SKU,
            WarehouseId = stockItem.WarehouseId,
            WarehouseName = (await _context.Warehouses.FindAsync(new object[] { stockItem.WarehouseId }, cancellationToken))?.Name ?? "Warehouse",
            QuantityOnHand = stockItem.QuantityOnHand,
            QuantityReserved = stockItem.QuantityReserved,
            QuantityAvailable = Math.Max(0, stockItem.QuantityOnHand - stockItem.QuantityReserved),
            ReorderLevel = stockItem.ReorderLevel
        };
    }

    public async Task<bool> TransferStockAsync(StockTransferRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
            throw new DomainException("Source and destination warehouses cannot be the same.");

        if (request.Quantity <= 0)
            throw new DomainException("Transfer quantity must be greater than zero.");

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Product), request.ProductId);

        var fromStock = await _context.StockItems.FirstOrDefaultAsync(s => s.ProductId == request.ProductId && s.WarehouseId == request.FromWarehouseId, cancellationToken)
            ?? throw new DomainException("Source warehouse does not have stock records for this product.");

        if (fromStock.QuantityAvailable < request.Quantity)
            throw new DomainException($"Insufficient available stock at source warehouse. Available: {fromStock.QuantityAvailable}, Requested: {request.Quantity}");

        var toStock = await _context.StockItems.FirstOrDefaultAsync(s => s.ProductId == request.ProductId && s.WarehouseId == request.ToWarehouseId, cancellationToken);
        if (toStock == null)
        {
            toStock = new StockItem
            {
                ProductId = request.ProductId,
                WarehouseId = request.ToWarehouseId,
                QuantityOnHand = 0,
                QuantityReserved = 0,
                ReorderLevel = product.ReorderLevel
            };
            _context.StockItems.Add(toStock);
        }

        var fromBefore = fromStock.QuantityOnHand;
        fromStock.QuantityOnHand -= request.Quantity;

        var toBefore = toStock.QuantityOnHand;
        toStock.QuantityOnHand += request.Quantity;

        // Dual Ledger Entries
        _context.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            WarehouseId = request.FromWarehouseId,
            MovementType = StockMovementType.TransferOut,
            QuantityChange = -request.Quantity,
            QuantityBefore = fromBefore,
            QuantityAfter = fromStock.QuantityOnHand,
            ReferenceType = "StockTransfer",
            Reason = $"Transfer to warehouse {request.ToWarehouseId}: {request.Reason}",
            CreatedBy = _currentUser.UserName ?? "Admin",
            CreatedAtUtc = DateTime.UtcNow
        });

        _context.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            WarehouseId = request.ToWarehouseId,
            MovementType = StockMovementType.TransferIn,
            QuantityChange = request.Quantity,
            QuantityBefore = toBefore,
            QuantityAfter = toStock.QuantityOnHand,
            ReferenceType = "StockTransfer",
            Reason = $"Transfer from warehouse {request.FromWarehouseId}: {request.Reason}",
            CreatedBy = _currentUser.UserName ?? "Admin",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.StockTransferred,
            "Inventory",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            after: new { request.FromWarehouseId, request.ToWarehouseId, request.Quantity, request.Reason },
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<PagedResult<StockMovementDto>> GetStockMovementsAsync(Guid? productId = null, Guid? warehouseId = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = _context.StockMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.Warehouse)
            .AsQueryable();

        if (productId.HasValue)
            query = query.Where(m => m.ProductId == productId.Value);

        if (warehouseId.HasValue)
            query = query.Where(m => m.WarehouseId == warehouseId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new StockMovementDto
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                SKU = m.Product.SKU,
                WarehouseId = m.WarehouseId,
                WarehouseName = m.Warehouse.Name,
                MovementType = m.MovementType,
                QuantityChange = m.QuantityChange,
                QuantityBefore = m.QuantityBefore,
                QuantityAfter = m.QuantityAfter,
                ReferenceType = m.ReferenceType,
                ReferenceId = m.ReferenceId,
                Reason = m.Reason,
                CreatedBy = m.CreatedBy,
                CreatedAtUtc = m.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovementDto>(items, totalCount, page, pageSize);
    }

    public async Task<List<LowStockAlertDto>> GetLowStockAlertsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && (p.StockQuantity - p.ReservedQuantity) <= p.ReorderLevel)
            .OrderBy(p => (p.StockQuantity - p.ReservedQuantity))
            .Take(limit)
            .ToListAsync(cancellationToken);

        return products.Select(p =>
        {
            var avail = Math.Max(0, p.StockQuantity - p.ReservedQuantity);
            var primaryImg = p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
                ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

            return new LowStockAlertDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU,
                ImageUrl = primaryImg,
                CurrentStock = avail,
                ReorderLevel = p.ReorderLevel,
                Status = avail == 0 ? "Critical" : (avail <= (p.ReorderLevel / 2) ? "Low" : "Medium")
            };
        }).ToList();
    }
}
