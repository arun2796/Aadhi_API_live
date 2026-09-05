using AadhiCrackers.Application.Common;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Marketing;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IBannerService
{
    Task<List<HomepageBannerDto>> GetBannersAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<HomepageBannerDto?> GetBannerByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<HomepageBannerDto> CreateBannerAsync(CreateHomepageBannerRequest request, CancellationToken cancellationToken = default);
    Task<HomepageBannerDto> UpdateBannerAsync(Guid id, UpdateHomepageBannerRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteBannerAsync(Guid id, CancellationToken cancellationToken = default);
}

public class BannerService : IBannerService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public BannerService(
        IApplicationDbContext context,
        IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<List<HomepageBannerDto>> GetBannersAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _context.HomepageBanners
            .AsNoTracking()
            .Where(b => !b.IsDeleted);

        if (activeOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(b => b.IsActive &&
                (!b.StartDateUtc.HasValue || b.StartDateUtc.Value <= now) &&
                (!b.EndDateUtc.HasValue || b.EndDateUtc.Value >= now));
        }

        var banners = await query
            .OrderBy(b => b.DisplayOrder)
            .ThenByDescending(b => b.CreatedAtUtc)
            .Select(b => new HomepageBannerDto
            {
                Id = b.Id,
                Title = b.Title,
                Subtitle = b.Subtitle,
                ImageUrl = b.ImageUrl,
                MobileImageUrl = b.MobileImageUrl,
                TargetUrl = b.TargetUrl,
                CtaText = b.CtaText,
                DisplayOrder = b.DisplayOrder,
                IsActive = b.IsActive,
                StartDateUtc = b.StartDateUtc,
                EndDateUtc = b.EndDateUtc,
                CreatedAtUtc = b.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return banners;
    }

    public async Task<HomepageBannerDto?> GetBannerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var banner = await _context.HomepageBanners
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken);

        if (banner == null) return null;

        return new HomepageBannerDto
        {
            Id = banner.Id,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            ImageUrl = banner.ImageUrl,
            MobileImageUrl = banner.MobileImageUrl,
            TargetUrl = banner.TargetUrl,
            CtaText = banner.CtaText,
            DisplayOrder = banner.DisplayOrder,
            IsActive = banner.IsActive,
            StartDateUtc = banner.StartDateUtc,
            EndDateUtc = banner.EndDateUtc,
            CreatedAtUtc = banner.CreatedAtUtc
        };
    }

    public async Task<HomepageBannerDto> CreateBannerAsync(CreateHomepageBannerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainException("Banner title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            throw new DomainException("Banner image URL is required.");
        }

        var banner = new HomepageBanner
        {
            Title = request.Title.Trim(),
            Subtitle = request.Subtitle?.Trim(),
            ImageUrl = ImageUrlNormalizer.Normalize(request.ImageUrl) ?? request.ImageUrl.Trim(),
            MobileImageUrl = ImageUrlNormalizer.Normalize(request.MobileImageUrl),
            TargetUrl = string.IsNullOrWhiteSpace(request.TargetUrl) ? "/products" : request.TargetUrl.Trim(),
            CtaText = string.IsNullOrWhiteSpace(request.CtaText) ? "Shop Now" : request.CtaText.Trim(),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            StartDateUtc = request.StartDateUtc,
            EndDateUtc = request.EndDateUtc
        };

        _context.HomepageBanners.Add(banner);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Marketing",
            nameof(HomepageBanner),
            banner.Id.ToString(),
            banner.Title,
            after: new { banner.Title, banner.IsActive, banner.DisplayOrder },
            cancellationToken: cancellationToken);

        return new HomepageBannerDto
        {
            Id = banner.Id,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            ImageUrl = banner.ImageUrl,
            MobileImageUrl = banner.MobileImageUrl,
            TargetUrl = banner.TargetUrl,
            CtaText = banner.CtaText,
            DisplayOrder = banner.DisplayOrder,
            IsActive = banner.IsActive,
            StartDateUtc = banner.StartDateUtc,
            EndDateUtc = banner.EndDateUtc,
            CreatedAtUtc = banner.CreatedAtUtc
        };
    }

    public async Task<HomepageBannerDto> UpdateBannerAsync(Guid id, UpdateHomepageBannerRequest request, CancellationToken cancellationToken = default)
    {
        var banner = await _context.HomepageBanners
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(HomepageBanner), id);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainException("Banner title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            throw new DomainException("Banner image URL is required.");
        }

        var oldTitle = banner.Title;
        banner.Title = request.Title.Trim();
        banner.Subtitle = request.Subtitle?.Trim();
        banner.ImageUrl = ImageUrlNormalizer.Normalize(request.ImageUrl) ?? request.ImageUrl.Trim();
        banner.MobileImageUrl = ImageUrlNormalizer.Normalize(request.MobileImageUrl);
        banner.TargetUrl = string.IsNullOrWhiteSpace(request.TargetUrl) ? "/products" : request.TargetUrl.Trim();
        banner.CtaText = string.IsNullOrWhiteSpace(request.CtaText) ? "Shop Now" : request.CtaText.Trim();
        banner.DisplayOrder = request.DisplayOrder;
        banner.IsActive = request.IsActive;
        banner.StartDateUtc = request.StartDateUtc;
        banner.EndDateUtc = request.EndDateUtc;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Marketing",
            nameof(HomepageBanner),
            banner.Id.ToString(),
            banner.Title,
            before: new { Title = oldTitle },
            after: new { banner.Title, banner.IsActive, banner.DisplayOrder },
            cancellationToken: cancellationToken);

        return new HomepageBannerDto
        {
            Id = banner.Id,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            ImageUrl = banner.ImageUrl,
            MobileImageUrl = banner.MobileImageUrl,
            TargetUrl = banner.TargetUrl,
            CtaText = banner.CtaText,
            DisplayOrder = banner.DisplayOrder,
            IsActive = banner.IsActive,
            StartDateUtc = banner.StartDateUtc,
            EndDateUtc = banner.EndDateUtc,
            CreatedAtUtc = banner.CreatedAtUtc
        };
    }

    public async Task<bool> DeleteBannerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var banner = await _context.HomepageBanners
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(HomepageBanner), id);

        banner.IsDeleted = true;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Marketing",
            nameof(HomepageBanner),
            banner.Id.ToString(),
            banner.Title,
            cancellationToken: cancellationToken);

        return true;
    }
}
