using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<StockItem> StockItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptItem> GoodsReceiptItems { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    string? Role { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string CorrelationId { get; }
    bool IsAuthenticated { get; }
}

public interface IIdentityService
{
    Task<AuthResponse> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default);
    Task<bool> UpdateUserRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<bool> ToggleUserStatusAsync(string userId, bool isActive, CancellationToken cancellationToken = default);
}

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendOrderConfirmationAsync(Order order, CancellationToken cancellationToken = default);
    Task SendOrderStatusUpdatedAsync(Order order, CancellationToken cancellationToken = default);
    Task SendLowStockAlertAsync(Product product, int currentStock, CancellationToken cancellationToken = default);
}

public interface ISearchService
{
    Task<PagedResult<Contracts.Catalog.ProductDto>> SearchProductsAsync(string query, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}

public interface IOutboxService
{
    Task EnqueueAsync(string type, object payload, CancellationToken cancellationToken = default);
}
