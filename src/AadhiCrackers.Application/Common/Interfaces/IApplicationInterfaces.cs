using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<LoginHistory> LoginHistories { get; }
    DbSet<RateLimitLog> RateLimitLogs { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<ProductReview> ProductReviews { get; }
    DbSet<PromotionRedemption> PromotionRedemptions { get; }
    DbSet<HomepageBanner> HomepageBanners { get; }
    DbSet<OtpVerification> OtpVerifications { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<Enquiry> Enquiries { get; }
    DbSet<EnquiryItem> EnquiryItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
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
    Task<AuthResponse> AuthenticateWithFirebaseAsync(FirebaseLoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> CreateStaffUserAsync(CreateStaffUserRequest request, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<AuthResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default);
    Task<bool> UpdateUserRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<bool> ToggleUserStatusAsync(string userId, bool isActive, CancellationToken cancellationToken = default);
    Task<List<LoginHistoryDto>> GetLoginHistoryAsync(CancellationToken cancellationToken = default);
    Task<List<RateLimitLogDto>> GetRateLimitLogsAsync(CancellationToken cancellationToken = default);

    Task<string?> GeneratePasswordResetOtpAsync(string identifier, CancellationToken cancellationToken = default);
    Task<string?> VerifyPasswordResetOtpAsync(string identifier, string otp, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordWithTokenAsync(string resetToken, string newPassword, CancellationToken cancellationToken = default);
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
    Task SendPaymentVerifiedAsync(Order order, CancellationToken cancellationToken = default);
    Task SendPaymentRejectedAsync(Order order, string reason, CancellationToken cancellationToken = default);
    Task SendLowStockAlertAsync(Product product, int currentStock, CancellationToken cancellationToken = default);
    Task SendPasswordResetOtpAsync(string recipient, string otpCode, CancellationToken cancellationToken = default);
}

public interface IOutboxService
{
    Task EnqueueAsync(string type, object payload, CancellationToken cancellationToken = default);
}

public interface IProductSearchService
{
    Task<PagedResult<Product>> SearchAsync(string query, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}

public interface ISearchService
{
    Task<PagedResult<AadhiCrackers.Contracts.Catalog.ProductDto>> SearchProductsAsync(string query, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
