using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Promotions;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IPromotionService
{
    Task<PagedResult<PromotionDto>> GetPromotionsAsync(int page = 1, int pageSize = 20, string? search = null, bool? activeOnly = null, CancellationToken cancellationToken = default);
    Task<PromotionDto?> GetPromotionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionDto> CreatePromotionAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionDto> UpdatePromotionAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default);
}

public class PromotionService : IPromotionService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public PromotionService(IApplicationDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<PagedResult<PromotionDto>> GetPromotionsAsync(int page = 1, int pageSize = 20, string? search = null, bool? activeOnly = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Promotions.Where(p => !p.IsDeleted).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpper();
            query = query.Where(p => p.Code.Contains(s) || p.Name.ToUpper().Contains(s));
        }

        if (activeOnly.HasValue && activeOnly.Value)
        {
            query = query.Where(p => p.IsActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapToDto(p))
            .ToListAsync(cancellationToken);

        return new PagedResult<PromotionDto>(items, totalCount, page, pageSize);
    }

    public async Task<PromotionDto?> GetPromotionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _context.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        return promo == null ? null : MapToDto(promo);
    }

    public async Task<PromotionDto> CreatePromotionAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpper();
        var exists = await _context.Promotions.AnyAsync(p => p.Code == code && !p.IsDeleted, cancellationToken);
        if (exists)
        {
            throw new DomainException($"Promotion with code '{code}' already exists.");
        }

        var promo = new Promotion
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MinimumOrderAmount = request.MinimumOrderAmount.HasValue ? Money.FromDecimal(request.MinimumOrderAmount.Value) : null,
            MaximumDiscount = request.MaximumDiscount.HasValue ? Money.FromDecimal(request.MaximumDiscount.Value) : null,
            StartDateUtc = request.StartDateUtc,
            EndDateUtc = request.EndDateUtc,
            UsageLimit = request.UsageLimit,
            UsedCount = 0,
            PerCustomerLimit = request.PerCustomerLimit,
            IsActive = request.IsActive,
            RowVersion = Guid.NewGuid()
        };

        _context.Promotions.Add(promo);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Promotions",
            nameof(Promotion),
            promo.Id.ToString(),
            promo.Code,
            after: new { promo.Code, promo.DiscountType, promo.DiscountValue, promo.UsageLimit, promo.PerCustomerLimit },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(promo);
    }

    public async Task<PromotionDto> UpdatePromotionAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Promotion), id);

        var before = new { promo.Name, promo.DiscountType, promo.DiscountValue, promo.IsActive };

        promo.Name = request.Name.Trim();
        promo.Description = request.Description?.Trim();
        promo.DiscountType = request.DiscountType;
        promo.DiscountValue = request.DiscountValue;
        promo.MinimumOrderAmount = request.MinimumOrderAmount.HasValue ? Money.FromDecimal(request.MinimumOrderAmount.Value) : null;
        promo.MaximumDiscount = request.MaximumDiscount.HasValue ? Money.FromDecimal(request.MaximumDiscount.Value) : null;
        promo.StartDateUtc = request.StartDateUtc;
        promo.EndDateUtc = request.EndDateUtc;
        promo.UsageLimit = request.UsageLimit;
        promo.PerCustomerLimit = request.PerCustomerLimit;
        promo.IsActive = request.IsActive;
        promo.RowVersion = Guid.NewGuid();
        promo.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Promotions",
            nameof(Promotion),
            promo.Id.ToString(),
            promo.Code,
            before: before,
            after: new { promo.Name, promo.DiscountType, promo.DiscountValue, promo.IsActive },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(promo);
    }

    public async Task<bool> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        if (promo == null) return false;

        promo.IsDeleted = true;
        promo.IsActive = false;
        promo.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Promotions",
            nameof(Promotion),
            promo.Id.ToString(),
            promo.Code,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static PromotionDto MapToDto(Promotion p)
    {
        return new PromotionDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            DiscountType = p.DiscountType,
            DiscountValue = p.DiscountValue,
            MinimumOrderAmount = p.MinimumOrderAmount?.ToDecimal(),
            MaximumDiscount = p.MaximumDiscount?.ToDecimal(),
            StartDateUtc = p.StartDateUtc,
            EndDateUtc = p.EndDateUtc,
            UsageLimit = p.UsageLimit,
            UsedCount = p.UsedCount,
            PerCustomerLimit = p.PerCustomerLimit,
            IsActive = p.IsActive
        };
    }
}
