using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IReviewService
{
    Task<PagedResult<ProductReviewDto>> GetReviewsAsync(Guid? productId = null, string? status = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<ProductReviewDto?> GetReviewByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductReviewDto> CreateReviewAsync(CreateProductReviewRequest request, CancellationToken cancellationToken = default);
    Task<ProductReviewDto> UpdateReviewStatusAsync(Guid id, UpdateReviewStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteReviewAsync(Guid id, CancellationToken cancellationToken = default);
}

public class ReviewService : IReviewService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _auditLog;

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending", "Approved", "Rejected", "Hidden"
    };

    public ReviewService(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IAuditLogService auditLog)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLog = auditLog;
    }

    public async Task<PagedResult<ProductReviewDto>> GetReviewsAsync(
        Guid? productId = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Where(r => !r.IsDeleted);

        if (productId.HasValue)
        {
            query = query.Where(r => r.ProductId == productId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }
        else if (!_currentUser.IsAuthenticated || (_currentUser.Role != "Admin" && _currentUser.Role != "SuperAdmin"))
        {
            // Public queries only get approved reviews
            query = query.Where(r => r.Status == "Approved");
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ProductReviewDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                ProductName = r.Product != null ? r.Product.Name : "Product",
                CustomerId = r.CustomerId,
                CustomerName = r.CustomerName,
                OrderId = r.OrderId,
                OrderItemId = r.OrderItemId,
                Title = r.Title,
                Rating = r.Rating,
                Comment = r.Comment,
                Status = r.Status,
                CreatedAtUtc = r.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductReviewDto>(items, totalCount, page, pageSize);
    }

    public async Task<ProductReviewDto?> GetReviewByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

        if (review == null) return null;

        return new ProductReviewDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            ProductName = review.Product != null ? review.Product.Name : "Product",
            CustomerId = review.CustomerId,
            CustomerName = review.CustomerName,
            OrderId = review.OrderId,
            OrderItemId = review.OrderItemId,
            Title = review.Title,
            Rating = review.Rating,
            Comment = review.Comment,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAtUtc
        };
    }

    public async Task<ProductReviewDto> CreateReviewAsync(CreateProductReviewRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Product), request.ProductId);

        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new DomainException("Rating must be between 1 and 5 stars.");
        }

        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            throw new DomainException("Review comment cannot be empty.");
        }

        Guid? customerId = null;
        var customerName = request.CustomerName?.Trim();

        // If authenticated customer, verify order delivery and prevent duplicates
        if (_currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.UserId == _currentUser.UserId && !c.IsDeleted, cancellationToken);

            if (customer != null)
            {
                customerId = customer.Id;
                if (string.IsNullOrWhiteSpace(customerName))
                {
                    customerName = customer.FullName;
                }

                // Check eligibility: must have a delivered order containing this product
                var hasDeliveredPurchase = await _context.Orders
                    .AsNoTracking()
                    .Where(o => o.CustomerId == customer.Id && o.OrderStatus == OrderStatus.Delivered && !o.IsDeleted)
                    .AnyAsync(o => o.Items.Any(i => i.ProductId == product.Id), cancellationToken);

                if (!hasDeliveredPurchase)
                {
                    throw new DomainException("Only verified customers who have received a delivered order for this product can submit a review.");
                }

                // Prevent duplicate review for the same customer and product
                var alreadyReviewed = await _context.ProductReviews
                    .AnyAsync(r => r.CustomerId == customer.Id && r.ProductId == product.Id && !r.IsDeleted, cancellationToken);

                if (alreadyReviewed)
                {
                    throw new DomainException("You have already submitted a review for this product.");
                }
            }
        }

        var review = new ProductReview
        {
            ProductId = product.Id,
            Product = product,
            CustomerId = customerId,
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName,
            OrderId = request.OrderId,
            OrderItemId = request.OrderItemId,
            Title = request.Title?.Trim() ?? string.Empty,
            Rating = request.Rating,
            Comment = request.Comment.Trim(),
            Status = "Pending" // Submitted reviews require moderation or auto-approval
        };

        _context.ProductReviews.Add(review);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(ProductReview),
            review.Id.ToString(),
            product.Name,
            after: new { review.Rating, review.Status, review.CustomerName },
            cancellationToken: cancellationToken);

        return new ProductReviewDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            ProductName = product.Name,
            CustomerId = review.CustomerId,
            CustomerName = review.CustomerName,
            OrderId = review.OrderId,
            OrderItemId = review.OrderItemId,
            Title = review.Title,
            Rating = review.Rating,
            Comment = review.Comment,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAtUtc
        };
    }

    public async Task<ProductReviewDto> UpdateReviewStatusAsync(Guid id, UpdateReviewStatusRequest request, CancellationToken cancellationToken = default)
    {
        var review = await _context.ProductReviews
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(ProductReview), id);

        if (!ValidStatuses.Contains(request.Status))
        {
            throw new DomainException($"Invalid review status '{request.Status}'. Valid statuses are: {string.Join(", ", ValidStatuses)}.");
        }

        var oldStatus = review.Status;
        review.Status = request.Status;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Catalog",
            nameof(ProductReview),
            review.Id.ToString(),
            review.Product?.Name ?? "Product",
            before: new { Status = oldStatus },
            after: new { Status = review.Status, request.ModerationNotes },
            cancellationToken: cancellationToken);

        return new ProductReviewDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            ProductName = review.Product?.Name ?? "Product",
            CustomerId = review.CustomerId,
            CustomerName = review.CustomerName,
            OrderId = review.OrderId,
            OrderItemId = review.OrderItemId,
            Title = review.Title,
            Rating = review.Rating,
            Comment = review.Comment,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAtUtc
        };
    }

    public async Task<bool> DeleteReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await _context.ProductReviews
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(ProductReview), id);

        review.IsDeleted = true;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(ProductReview),
            review.Id.ToString(),
            review.CustomerName,
            cancellationToken: cancellationToken);

        return true;
    }
}
