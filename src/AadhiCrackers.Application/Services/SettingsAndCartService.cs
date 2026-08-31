using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Audit;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface ISettingsService
{
    Task<List<SystemSettingDto>> GetSettingsAsync(string? group = null, CancellationToken cancellationToken = default);
    Task<string> GetSettingValueAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default);
    Task<bool> UpdateSettingAsync(string key, string value, CancellationToken cancellationToken = default);
}

public class SettingsService : ISettingsService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public SettingsService(IApplicationDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<List<SystemSettingDto>> GetSettingsAsync(string? group = null, CancellationToken cancellationToken = default)
    {
        var query = _context.SystemSettings.AsNoTracking().Where(s => !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(group))
            query = query.Where(s => s.Group.ToLower() == group.Trim().ToLower());

        return await query
            .OrderBy(s => s.Group)
            .ThenBy(s => s.Key)
            .Select(s => new SystemSettingDto
            {
                Id = s.Id,
                Key = s.Key,
                Value = s.IsEncrypted ? "********" : s.Value,
                Group = s.Group,
                Description = s.Description,
                IsEncrypted = s.IsEncrypted,
                UpdatedAtUtc = s.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<string> GetSettingValueAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
    {
        var setting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key.ToLower() == key.ToLower() && !s.IsDeleted, cancellationToken);
        return setting?.Value ?? defaultValue;
    }

    public async Task<bool> UpdateSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key.ToLower() == key.ToLower() && !s.IsDeleted, cancellationToken);
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = key.Trim(),
                Value = value.Trim(),
                Group = "General"
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            var before = setting.Value;
            setting.Value = value.Trim();
            setting.UpdatedAtUtc = DateTime.UtcNow;

            await _auditLog.LogAsync(
                AuditAction.SettingsChanged,
                "Settings",
                nameof(SystemSetting),
                setting.Id.ToString(),
                setting.Key,
                before: new { Value = setting.IsEncrypted ? "***" : before },
                after: new { Value = setting.IsEncrypted ? "***" : value },
                cancellationToken: cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public interface ICartService
{
    Task<CartDto> CalculateCartAsync(List<AddToCartRequest> items, string? couponCode = null, CancellationToken cancellationToken = default);
}

public class CartService : ICartService
{
    private readonly IApplicationDbContext _context;

    public CartService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CartDto> CalculateCartAsync(List<AddToCartRequest> items, string? couponCode = null, CancellationToken cancellationToken = default)
    {
        var result = new CartDto();
        if (items.Count == 0) return result;

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Images)
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var item in items)
        {
            if (products.TryGetValue(item.ProductId, out var p))
            {
                var primaryImg = p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
                    ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

                var avail = Math.Max(0, p.StockQuantity - p.ReservedQuantity);
                var qty = Math.Min(item.Quantity, Math.Max(1, avail));

                result.Items.Add(new CartItemDto
                {
                    ProductId = p.Id,
                    SKU = p.SKU,
                    Name = p.Name,
                    ImageUrl = primaryImg,
                    UnitPrice = p.Price.ToDecimal(),
                    CompareAtPrice = p.CompareAtPrice?.ToDecimal(),
                    Quantity = qty,
                    MaxStock = avail
                });
            }
        }

        // Coupon calculation
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code.ToUpper() == couponCode.Trim().ToUpper() && p.IsActive, cancellationToken);
            if (promo != null && promo.IsValidForOrder(Money.FromDecimal(result.Subtotal)))
            {
                result.CouponCode = promo.Code;
                result.Discount = promo.CalculateDiscount(Money.FromDecimal(result.Subtotal)).ToDecimal();
            }
        }

        return result;
    }
}
