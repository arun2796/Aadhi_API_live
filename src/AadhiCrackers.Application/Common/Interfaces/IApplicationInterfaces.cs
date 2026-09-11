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
    DbSet<ProductComboItem> ProductComboItems { get; }
    DbSet<ProductReview> ProductReviews { get; }
    DbSet<PromotionRedemption> PromotionRedemptions { get; }
    DbSet<HomepageBanner> HomepageBanners { get; }
    DbSet<OtpVerification> OtpVerifications { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<Notification> Notifications { get; }

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

/// <summary>
/// The outcome of storing one object. <paramref name="Url"/> is what a browser loads; for the R2
/// provider it is absolute and permanent, for the local provider it is the site-relative
/// /storage/... path. <paramref name="Key"/> is the provider-side object key (no leading slash)
/// and is what <see cref="IFileStorageService.DeleteFileAsync"/> accepts.
/// </summary>
public sealed record StoredFileResult(string Url, string Key, string ContentType, long SizeBytes);

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder = "products", CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an already-validated object and returns its public URL, key, content type and size.
    ///
    /// WHY THIS EXISTS ALONGSIDE SaveFileAsync. Object storage needs two things the old signature
    /// cannot carry: (1) the content type has to be written onto the object itself — R2/S3 serve
    /// back exactly the Content-Type they were given, so a wrong or missing one makes the browser
    /// download the file instead of rendering it, and (2) the caller needs the key and the public
    /// URL as separate values (a single returned string cannot be split back apart reliably once
    /// an absolute CDN URL is involved). The caller passes the content type and extension it
    /// SNIFFED from the bytes, never what the client claimed.
    ///
    /// The object key is generated by the implementation (random + the supplied extension); the
    /// user-supplied filename is never trusted or reused.
    /// </summary>
    Task<StoredFileResult> SaveObjectAsync(Stream fileStream, string folder, string contentType, string extension, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendOrderConfirmationAsync(Order order, CancellationToken cancellationToken = default);
    /// <summary>
    /// Raised when an order moves to a new status. <paramref name="newStatus"/> is the status the
    /// EVENT carried; it is preferred over the order row because several status events can be
    /// pending in the outbox at once while the row already shows only the latest one. Falls back to
    /// the order's current status when not supplied.
    /// </summary>
    Task SendOrderStatusUpdatedAsync(Order order, string? newStatus = null, CancellationToken cancellationToken = default);
    Task SendPaymentVerifiedAsync(Order order, CancellationToken cancellationToken = default);
    Task SendPaymentRejectedAsync(Order order, string reason, CancellationToken cancellationToken = default);
    Task SendOrderDispatchedAsync(Order order, string carrierName, string trackingNumber, string? carrierPhone, string? carrierAddress, CancellationToken cancellationToken = default);
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
