using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using AadhiCrackers.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

public class PurchaseAndGoodsReceiptWorkflowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AadhiDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;
    private readonly IBusinessNumberGenerator _numberGenerator;
    private readonly IPurchaseService _purchaseService;

    public PurchaseAndGoodsReceiptWorkflowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AadhiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AadhiDbContext(options);
        _context.Database.EnsureCreated();

        _currentUser = new TestCurrentUserService();
        var outbox = new OutboxService(_context);
        _auditLog = new AuditLogService(_context, _currentUser, outbox);
        _numberGenerator = new BusinessNumberGenerator(_context);
        _purchaseService = new PurchaseService(_context, _currentUser, _auditLog, _numberGenerator);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? Email => "admin@aadhicrackers.com";
        public string? UserName => "PurchasingAdmin";
        public string? Role => "Admin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "xUnit";
        public string CorrelationId => Guid.NewGuid().ToString();
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task PurchaseOrder_ApprovalLifecycle_TransitionsCorrectly()
    {
        // 1. Arrange category, supplier, warehouse, product
        var category = new Category { Name = "Crackers", Slug = "crackers-1", IsActive = true };
        _context.Categories.Add(category);

        var supplier = new Supplier
        {
            Code = "SUP-001",
            Name = "Standard Fireworks",
            Phone = "9876543210"
        };
        _context.Suppliers.Add(supplier);

        var warehouse = new Warehouse
        {
            Code = "WH-MAIN",
            Name = "Main Depot",
            Phone = "9876543210",
            IsActive = true,
            IsPrimary = true
        };
        _context.Warehouses.Add(warehouse);

        var product = new Product
        {
            CategoryId = category.Id,
            SKU = "CRK-1000",
            Name = "1000 Wala Crackers",
            Price = Money.FromDecimal(500m),
            CostPrice = Money.FromDecimal(250m),
            StockQuantity = 0,
            ReservedQuantity = 0,
            ReorderLevel = 10,
            IsActive = true
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // 2. Create Draft PO
        var createPoReq = new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            WarehouseId = warehouse.Id,
            Items = new List<CreatePurchaseOrderItemRequest>
            {
                new CreatePurchaseOrderItemRequest
                {
                    ProductId = product.Id,
                    UnitPrice = 250m,
                    Quantity = 100
                }
            }
        };

        var po = await _purchaseService.CreatePurchaseOrderAsync(createPoReq);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.StartsWith("PO-", po.PoNumber);

        // 3. Draft PO cannot receive goods
        var invalidGrnReq = new CreateGoodsReceiptRequest
        {
            PurchaseOrderId = po.Id,
            Items = new List<CreateGoodsReceiptItemRequest>
            {
                new CreateGoodsReceiptItemRequest
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    ProductId = product.Id,
                    QuantityReceived = 100,
                    UnitPrice = 250m
                }
            }
        };
        await Assert.ThrowsAsync<DomainException>(() => _purchaseService.CreateGoodsReceiptAsync(invalidGrnReq));

        // 4. Submit PO
        var submittedPo = await _purchaseService.SubmitPurchaseOrderAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.Submitted, submittedPo.Status);
        Assert.NotNull(submittedPo.SubmittedAtUtc);

        // 5. Approve PO
        var approvedPo = await _purchaseService.ApprovePurchaseOrderAsync(po.Id, new ApprovePurchaseOrderRequest { Notes = "Approved by manager" });
        Assert.Equal(PurchaseOrderStatus.Approved, approvedPo.Status);
        Assert.NotNull(approvedPo.ApprovedAtUtc);
    }

    [Fact]
    public async Task GoodsReceipt_PartialAndDamagedInspection_UpdatesStockAndLedgerAccurately()
    {
        // 1. Arrange category, supplier, warehouse, product
        var category = new Category { Name = "Bombs", Slug = "bombs-1", IsActive = true };
        _context.Categories.Add(category);

        var supplier = new Supplier
        {
            Code = "SUP-002",
            Name = "Ayyan Fireworks",
            Phone = "9876543211"
        };
        _context.Suppliers.Add(supplier);

        var warehouse = new Warehouse
        {
            Code = "WH-SIVAKASI",
            Name = "Sivakasi Godown",
            Phone = "9876543211",
            IsActive = true,
            IsPrimary = true
        };
        _context.Warehouses.Add(warehouse);

        var product = new Product
        {
            CategoryId = category.Id,
            SKU = "BMB-HYDRO",
            Name = "Hydro Bomb Box",
            Price = Money.FromDecimal(300m),
            CostPrice = Money.FromDecimal(150m),
            StockQuantity = 10,
            ReservedQuantity = 0,
            ReorderLevel = 20,
            IsActive = true
        };
        _context.Products.Add(product);
        _context.StockItems.Add(new StockItem
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            QuantityOnHand = 10,
            QuantityReserved = 0,
            ReorderLevel = 20
        });
        await _context.SaveChangesAsync();

        // 2. Create and Approve PO for 100 units
        var po = await _purchaseService.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            WarehouseId = warehouse.Id,
            Items = new List<CreatePurchaseOrderItemRequest>
            {
                new CreatePurchaseOrderItemRequest
                {
                    ProductId = product.Id,
                    UnitPrice = 150m,
                    Quantity = 100
                }
            }
        });
        await _purchaseService.SubmitPurchaseOrderAsync(po.Id);
        await _purchaseService.ApprovePurchaseOrderAsync(po.Id, new ApprovePurchaseOrderRequest());

        // 3. First GRN: Receive 50 units (45 accepted, 5 damaged in transit)
        var grn1 = await _purchaseService.CreateGoodsReceiptAsync(new CreateGoodsReceiptRequest
        {
            PurchaseOrderId = po.Id,
            Items = new List<CreateGoodsReceiptItemRequest>
            {
                new CreateGoodsReceiptItemRequest
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    ProductId = product.Id,
                    QuantityReceived = 50,
                    QuantityAccepted = 45,
                    QuantityDamaged = 5,
                    RejectionReason = "Crushed packaging",
                    UnitPrice = 150m
                }
            }
        });

        Assert.StartsWith("GRN-", grn1.ReceiptNumber);
        Assert.Equal(45, grn1.Items[0].QuantityAccepted);
        Assert.Equal(5, grn1.Items[0].QuantityDamaged);

        // Check PO Status is PartiallyReceived
        var poAfterGrn1 = await _purchaseService.GetPurchaseOrderByIdAsync(po.Id);
        Assert.NotNull(poAfterGrn1);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, poAfterGrn1.Status);
        Assert.Equal(50, poAfterGrn1.Items[0].QuantityReceived);

        // Check StockItem: 10 initial + 45 accepted = 55 on hand
        var stock1 = await _context.StockItems.FirstAsync(s => s.ProductId == product.Id && s.WarehouseId == warehouse.Id);
        Assert.Equal(55, stock1.QuantityOnHand);

        // Check Stock Ledger Movements
        var movements1 = await _context.StockMovements.Where(m => m.ProductId == product.Id).ToListAsync();
        Assert.Contains(movements1, m => m.MovementType == StockMovementType.Purchase && m.QuantityChange == 45);
        Assert.Contains(movements1, m => m.MovementType == StockMovementType.Damage && m.QuantityChange == 0);

        // 4. Second GRN: Receive remaining 50 units (all 50 accepted)
        var grn2 = await _purchaseService.CreateGoodsReceiptAsync(new CreateGoodsReceiptRequest
        {
            PurchaseOrderId = po.Id,
            Items = new List<CreateGoodsReceiptItemRequest>
            {
                new CreateGoodsReceiptItemRequest
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    ProductId = product.Id,
                    QuantityReceived = 50,
                    QuantityAccepted = 50,
                    QuantityDamaged = 0,
                    UnitPrice = 150m
                }
            }
        });

        // Check PO Status is Received
        var poAfterGrn2 = await _purchaseService.GetPurchaseOrderByIdAsync(po.Id);
        Assert.NotNull(poAfterGrn2);
        Assert.Equal(PurchaseOrderStatus.Received, poAfterGrn2.Status);
        Assert.Equal(100, poAfterGrn2.Items[0].QuantityReceived);

        // Check Final StockItem: 55 + 50 = 105
        var finalStock = await _context.StockItems.FirstAsync(s => s.ProductId == product.Id && s.WarehouseId == warehouse.Id);
        Assert.Equal(105, finalStock.QuantityOnHand);
    }
}
