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
    Task<Dictionary<string, string>> GetPublicSettingsAsync(CancellationToken cancellationToken = default);
}

public class SettingsService : ISettingsService
{
    // Hard whitelist for the anonymous storefront settings endpoint.
    // ONLY keys matching these prefixes/exact keys are ever exposed publicly —
    // never widen this list with security-sensitive groups (Jwt, RateLimiting, Smtp, etc.).
    // "Payment." carries the bank/UPI details the storefront prints on the payment screen
    // (bank name, account name, account number, IFSC). Any payment SECRET (gateway keys,
    // webhook signing secrets) must be stored with IsEncrypted = true — encrypted settings
    // are filtered out of this endpoint below and never leave the server.
    private static readonly string[] PublicKeyPrefixes =
    {
        "Store.",
        "Website.",
        "Delivery.",
        "Shipping.",
        "Payment."
    };

    // Individually vetted non-prefixed keys. "Order.PackingChargePercent" is exposed so a client
    // can LABEL the packing line (e.g. "Packing Charges (1.5%)"); the authoritative amount always
    // comes from POST /cart/calculate, which now returns packingCharges and grandTotal outright.
    // Only this one key from the "Order." group is public — the group as a whole is not.
    private static readonly string[] PublicExactKeys =
    {
        "DeliveryZones.Config",
        "Order.PackingChargePercent"
    };

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

    public async Task<Dictionary<string, string>> GetPublicSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _context.SystemSettings
            .AsNoTracking()
            .Where(s => !s.IsDeleted && !s.IsEncrypted)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(cancellationToken);

        return settings
            .Where(s =>
                PublicExactKeys.Any(k => string.Equals(k, s.Key, StringComparison.OrdinalIgnoreCase)) ||
                PublicKeyPrefixes.Any(p => s.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(s => s.Key, s => s.Value);
    }

    public async Task<bool> UpdateSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
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
    private readonly IOrderPricingService _pricing;
    private readonly ICurrentUserService? _currentUser;

    public CartService(IApplicationDbContext context, IOrderPricingService? pricing = null, ICurrentUserService? currentUser = null)
    {
        _context = context;
        _pricing = pricing ?? new OrderPricingService(context);
        _currentUser = currentUser;
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

        // Lines are built exactly as OrderService.CreateOrderAsync builds them: unit price straight
        // off the product, tax at the product's own rate. Same inputs => same money.
        var lines = new List<PricedLine>();
        var itemsSubtotal = Money.Zero();

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

                var line = new PricedLine(p.Price, qty, p.TaxRate);
                lines.Add(line);
                itemsSubtotal += line.LineTotalBeforeTax;
            }
        }

        // Coupon resolution runs through the same gate the order does — including the
        // per-customer usage limit when the caller is signed in.
        var customerId = await _pricing.ResolveOrderCustomerIdAsync(_currentUser?.Email, cancellationToken);
        var (promo, discount) = await _pricing.ResolveCouponAsync(couponCode, itemsSubtotal, customerId, cancellationToken);
        if (promo != null)
        {
            result.CouponCode = promo.Code;
        }

        var packingChargePercent = await _pricing.GetPackingChargePercentAsync(cancellationToken);
        var totals = _pricing.CalculateTotals(lines, discount, packingChargePercent);

        result.ItemsSubtotal = totals.ItemsSubtotal.ToDecimal();
        result.Discount = totals.Discount.ToDecimal();
        result.Tax = totals.Tax.ToDecimal();
        result.PackingCharges = totals.PackingCharges.ToDecimal();
        result.PackingChargePercent = totals.PackingChargePercent;
        // Sivakasi Cracker deliveries are strictly dispatched on a To-Pay transport basis or local pickup; NO online delivery charges are charged.
        result.ShippingCharge = totals.ShippingCharge.ToDecimal();
        result.GrandTotal = totals.GrandTotal.ToDecimal();

        return result;
    }
}
