using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Customers;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IWishlistService
{
    Task<List<WishlistItemDto>> GetMyWishlistAsync(CancellationToken cancellationToken = default);
    Task<WishlistItemDto> AddAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid productId, CancellationToken cancellationToken = default);
}

public interface IAddressService
{
    Task<List<AddressDto>> GetMyAddressesAsync(CancellationToken cancellationToken = default);
    Task<AddressDto> CreateAsync(AddressDto request, CancellationToken cancellationToken = default);
    Task<AddressDto> UpdateAsync(Guid id, AddressDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default);
}

public class WishlistService : IWishlistService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public WishlistService(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<WishlistItemDto>> GetMyWishlistAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken);
        if (customer == null)
        {
            return new List<WishlistItemDto>();
        }

        var items = await _context.WishlistItems
            .AsNoTracking()
            .Include(w => w.Product)
                .ThenInclude(p => p.Images)
            .Where(w => w.CustomerId == customer.Id)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return items.Where(w => w.Product != null).Select(MapToDto).ToList();
    }

    public async Task<WishlistItemDto> AddAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken)
            ?? throw new DomainException("No customer profile found for the current user.");

        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Product), productId);

        // Idempotent: adding twice returns the existing entry
        var existing = await _context.WishlistItems
            .FirstOrDefaultAsync(w => w.CustomerId == customer.Id && w.ProductId == productId, cancellationToken);
        if (existing != null)
        {
            existing.Product = product;
            return MapToDto(existing);
        }

        var item = new WishlistItem
        {
            CustomerId = customer.Id,
            ProductId = productId
        };
        _context.WishlistItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        item.Product = product;
        return MapToDto(item);
    }

    public async Task<bool> RemoveAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken);
        if (customer == null)
        {
            return false;
        }

        var existing = await _context.WishlistItems
            .FirstOrDefaultAsync(w => w.CustomerId == customer.Id && w.ProductId == productId, cancellationToken);
        if (existing == null)
        {
            return true; // idempotent delete
        }

        _context.WishlistItems.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Customer?> ResolveCurrentCustomerAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var email = _currentUser.Email;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email)) return null;

        return await _context.Customers
            .FirstOrDefaultAsync(c =>
                (userId != null && c.UserId == userId) ||
                (email != null && c.Email.ToLower() == email.ToLower()), cancellationToken);
    }

    private static WishlistItemDto MapToDto(WishlistItem w)
    {
        var product = w.Product;
        var price = product.Price.ToDecimal();
        var mrp = product.CompareAtPrice?.ToDecimal();
        var discountPercent = mrp.HasValue && mrp.Value > price && mrp.Value > 0
            ? (int)Math.Round((1 - (price / mrp.Value)) * 100)
            : 0;
        var imageUrl = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
            ?? product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

        return new WishlistItemDto
        {
            Id = w.Id,
            ProductId = w.ProductId,
            CreatedAt = w.CreatedAtUtc,
            Product = new WishlistProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                Price = price,
                Mrp = mrp,
                DiscountPercent = discountPercent,
                ImageUrl = imageUrl,
                InStock = product.AvailableQuantity > 0 && product.IsActive
            }
        };
    }
}

public class AddressService : IAddressService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AddressService(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<AddressDto>> GetMyAddressesAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken);
        if (customer == null)
        {
            return new List<AddressDto>();
        }

        var addresses = await _context.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.CustomerId == customer.Id)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return addresses.Select(MapToDto).ToList();
    }

    public async Task<AddressDto> CreateAsync(AddressDto request, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken)
            ?? throw new DomainException("No customer profile found for the current user.");

        var hasAnyAddress = await _context.CustomerAddresses
            .AnyAsync(a => a.CustomerId == customer.Id, cancellationToken);
        var makeDefault = request.IsDefault || !hasAnyAddress;

        if (makeDefault)
        {
            await ClearDefaultAsync(customer.Id, cancellationToken);
        }

        var entity = new CustomerAddress
        {
            CustomerId = customer.Id,
            AddressType = AddressType.Both,
            Label = NormalizeLabel(request.Label),
            IsDefault = makeDefault,
            Address = MapToAddress(request)
        };

        _context.CustomerAddresses.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AddressDto> UpdateAsync(Guid id, AddressDto request, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken)
            ?? throw new DomainException("No customer profile found for the current user.");

        // IDOR-safe: only the owning customer's address can be updated
        var entity = await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customer.Id, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(CustomerAddress), id);

        entity.Label = NormalizeLabel(request.Label);
        entity.Address = MapToAddress(request);
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (request.IsDefault && !entity.IsDefault)
        {
            await ClearDefaultAsync(customer.Id, cancellationToken);
            entity.IsDefault = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken);
        if (customer == null)
        {
            return false;
        }

        var entity = await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customer.Id, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        var wasDefault = entity.IsDefault;
        _context.CustomerAddresses.Remove(entity);

        if (wasDefault)
        {
            // Promote the most recent remaining address so the customer always has a default
            var nextDefault = await _context.CustomerAddresses
                .Where(a => a.CustomerId == customer.Id && a.Id != id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (nextDefault != null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCurrentCustomerAsync(cancellationToken);
        if (customer == null)
        {
            return false;
        }

        var entity = await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customer.Id, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        await ClearDefaultAsync(customer.Id, cancellationToken);
        entity.IsDefault = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ClearDefaultAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var defaults = await _context.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsDefault)
            .ToListAsync(cancellationToken);
        foreach (var address in defaults)
        {
            address.IsDefault = false;
            address.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<Customer?> ResolveCurrentCustomerAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var email = _currentUser.Email;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email)) return null;

        return await _context.Customers
            .FirstOrDefaultAsync(c =>
                (userId != null && c.UserId == userId) ||
                (email != null && c.Email.ToLower() == email.ToLower()), cancellationToken);
    }

    private static string NormalizeLabel(string? label)
    {
        var value = label?.Trim();
        return value?.ToLowerInvariant() switch
        {
            "office" => "Office",
            "other" => "Other",
            _ => "Home"
        };
    }

    private static Address MapToAddress(AddressDto dto) => new(
        dto.FullName,
        dto.Phone,
        dto.AddressLine1,
        dto.AddressLine2,
        dto.City,
        dto.State,
        dto.Pincode);

    private static AddressDto MapToDto(CustomerAddress entity) => new()
    {
        Id = entity.Id,
        Label = entity.Label ?? "Home",
        FullName = entity.Address.FullName,
        Phone = entity.Address.Phone,
        AddressLine1 = entity.Address.AddressLine1,
        AddressLine2 = entity.Address.AddressLine2,
        City = entity.Address.City,
        State = entity.Address.State,
        Pincode = entity.Address.PostalCode,
        IsDefault = entity.IsDefault
    };
}
