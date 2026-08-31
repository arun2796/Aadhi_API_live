using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface ICatalogService
{
    Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<CategoryDto> UpdateCategoryAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<BrandDto>> GetBrandsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<BrandDto> CreateBrandAsync(CreateBrandRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<ProductDto>> GetProductsAsync(ProductFilterRequest filter, CancellationToken cancellationToken = default);
    Task<ProductDetailDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<ProductDetailDto> UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<ProductDto>> GetFeaturedProductsAsync(int count = 8, CancellationToken cancellationToken = default);
    Task<List<ProductDto>> GetBestSellersAsync(int count = 8, CancellationToken cancellationToken = default);
    Task<List<ProductDto>> GetNewArrivalsAsync(int count = 8, CancellationToken cancellationToken = default);
    Task<List<ProductDto>> GetGiftBoxesAsync(int count = 8, CancellationToken cancellationToken = default);
    Task<List<ProductDto>> GetComboOffersAsync(int count = 8, CancellationToken cancellationToken = default);
}

public class CatalogService : ICatalogService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public CatalogService(IApplicationDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories
            .AsNoTracking()
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive && !c.IsDeleted);
        }
        else
        {
            query = query.Where(c => !c.IsDeleted);
        }

        var categories = await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // Build hierarchical tree for root categories (ParentCategoryId == null)
        return categories
            .Where(c => c.ParentCategoryId == null)
            .Select(c => MapCategoryToDto(c, categories))
            .ToList();
    }

    private static CategoryDto MapCategoryToDto(Category c, List<Category> allCategories)
    {
        var subCats = allCategories
            .Where(sub => sub.ParentCategoryId == c.Id && !sub.IsDeleted)
            .OrderBy(sub => sub.DisplayOrder)
            .Select(sub => MapCategoryToDto(sub, allCategories))
            .ToList();

        return new CategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            Description = c.Description,
            ParentCategoryId = c.ParentCategoryId,
            ParentCategoryName = c.ParentCategory?.Name,
            ImageUrl = c.ImageUrl,
            DisplayOrder = c.DisplayOrder,
            IsActive = c.IsActive,
            ProductCount = c.Products.Count(p => p.IsActive && !p.IsDeleted),
            SubCategories = subCats
        };
    }

    public async Task<CategoryDto?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var c = await _context.Categories
            .AsNoTracking()
            .Include(x => x.ParentCategory)
            .Include(x => x.Products)
            .FirstOrDefaultAsync(x => x.Slug.ToLower() == slug.ToLower() && !x.IsDeleted, cancellationToken);

        if (c == null) return null;

        var all = await _context.Categories.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync(cancellationToken);
        return MapCategoryToDto(c, all);
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var slug = GenerateSlug(request.Name);
        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            ImageUrl = request.ImageUrl,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            after: category,
            cancellationToken: cancellationToken);

        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            ParentCategoryId = category.ParentCategoryId,
            ImageUrl = category.ImageUrl,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        };
    }

    public async Task<CategoryDto> UpdateCategoryAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Category), request.Id);

        var before = new { category.Name, category.Slug, category.Description, category.IsActive };

        category.Name = request.Name.Trim();
        category.Slug = GenerateSlug(request.Name);
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.ImageUrl = request.ImageUrl;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.SeoTitle = request.SeoTitle;
        category.SeoDescription = request.SeoDescription;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            before: before,
            after: category,
            cancellationToken: cancellationToken);

        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            ParentCategoryId = category.ParentCategoryId,
            ImageUrl = category.ImageUrl,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        };
    }

    public async Task<bool> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
        if (category == null) return false;

        category.IsDeleted = true;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<List<BrandDto>> GetBrandsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Brands.AsNoTracking().Include(b => b.Products).AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive && !b.IsDeleted);
        }
        else
        {
            query = query.Where(b => !b.IsDeleted);
        }

        return await query
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto
            {
                Id = b.Id,
                Name = b.Name,
                Slug = b.Slug,
                Description = b.Description,
                LogoUrl = b.LogoUrl,
                IsActive = b.IsActive,
                ProductCount = b.Products.Count(p => p.IsActive && !p.IsDeleted)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BrandDto> CreateBrandAsync(CreateBrandRequest request, CancellationToken cancellationToken = default)
    {
        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Slug = GenerateSlug(request.Name),
            Description = request.Description,
            LogoUrl = request.LogoUrl,
            IsActive = request.IsActive
        };

        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Brand),
            brand.Id.ToString(),
            brand.Name,
            after: brand,
            cancellationToken: cancellationToken);

        return new BrandDto
        {
            Id = brand.Id,
            Name = brand.Name,
            Slug = brand.Slug,
            Description = brand.Description,
            LogoUrl = brand.LogoUrl,
            IsActive = brand.IsActive
        };
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(ProductFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(s) ||
                p.SKU.ToLower().Contains(s) ||
                p.Category.Name.ToLower().Contains(s) ||
                (p.Brand != null && p.Brand.Name.ToLower().Contains(s)));
        }

        if (filter.CategoryId.HasValue)
        {
            var catId = filter.CategoryId.Value;
            var subCategoryIds = await _context.Categories
                .Where(c => c.ParentCategoryId == catId && !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            subCategoryIds.Add(catId);
            query = query.Where(p => subCategoryIds.Contains(p.CategoryId));
        }
        else if (!string.IsNullOrWhiteSpace(filter.CategorySlug))
        {
            var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Slug.ToLower() == filter.CategorySlug.ToLower() && !c.IsDeleted, cancellationToken);
            if (cat != null)
            {
                var subCategoryIds = await _context.Categories
                    .Where(c => c.ParentCategoryId == cat.Id && !c.IsDeleted)
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken);

                subCategoryIds.Add(cat.Id);
                query = query.Where(p => subCategoryIds.Contains(p.CategoryId));
            }
        }

        if (filter.BrandId.HasValue)
            query = query.Where(p => p.BrandId == filter.BrandId.Value);

        if (filter.MinPrice.HasValue)
        {
            var minMinor = (long)(filter.MinPrice.Value * 100);
            query = query.Where(p => p.Price.AmountMinor >= minMinor);
        }

        if (filter.MaxPrice.HasValue)
        {
            var maxMinor = (long)(filter.MaxPrice.Value * 100);
            query = query.Where(p => p.Price.AmountMinor <= maxMinor);
        }

        if (filter.InStockOnly == true)
            query = query.Where(p => (p.StockQuantity - p.ReservedQuantity) > 0);

        if (filter.IsFeatured == true)
            query = query.Where(p => p.IsFeatured);

        if (filter.IsBestSeller == true)
            query = query.Where(p => p.IsBestSeller);

        if (filter.IsNewArrival == true)
            query = query.Where(p => p.IsNewArrival);

        // Sorting
        query = filter.SortBy?.ToLower() switch
        {
            "price_asc" => query.OrderBy(p => p.Price.AmountMinor),
            "price_desc" => query.OrderByDescending(p => p.Price.AmountMinor),
            "name_asc" => query.OrderBy(p => p.Name),
            "new" => query.OrderByDescending(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.IsBestSeller).ThenByDescending(p => p.IsFeatured).ThenBy(p => p.Name)
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<ProductDetailDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Slug.ToLower() == slug.ToLower() && !p.IsDeleted, cancellationToken);

        if (product == null) return null;

        var related = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id && p.IsActive && !p.IsDeleted)
            .Take(4)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

        return MapToProductDetailDto(product, related);
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (product == null) return null;

        return MapToProductDetailDto(product, new List<ProductDto>());
    }

    public async Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var slug = GenerateSlug(request.Name);

        // Ensure unique SKU
        if (await _context.Products.AnyAsync(p => p.SKU.ToLower() == request.SKU.ToLower() && !p.IsDeleted, cancellationToken))
        {
            throw new DomainException($"Product with SKU '{request.SKU}' already exists.");
        }

        var product = new Product
        {
            SKU = request.SKU.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Price = Money.FromDecimal(request.Price),
            CompareAtPrice = request.CompareAtPrice.HasValue ? Money.FromDecimal(request.CompareAtPrice.Value) : null,
            CostPrice = Money.FromDecimal(request.CostPrice),
            TaxRate = request.TaxRate,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            StockQuantity = request.StockQuantity,
            ReorderLevel = request.ReorderLevel,
            MinOrderQuantity = request.MinOrderQuantity,
            MaxOrderQuantity = request.MaxOrderQuantity,
            Unit = request.Unit,
            WeightKg = request.WeightKg,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            IsBestSeller = request.IsBestSeller,
            IsNewArrival = request.IsNewArrival,
            SafetyInformation = request.SafetyInformation
        };

        int sortOrder = 0;
        foreach (var url in request.ImageUrls)
        {
            product.Images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = url,
                SortOrder = sortOrder,
                IsPrimary = sortOrder == 0
            });
            sortOrder++;
        }

        _context.Products.Add(product);

        // Default warehouse stock item
        var primaryWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsPrimary && !w.IsDeleted, cancellationToken);
        if (primaryWarehouse != null && request.StockQuantity > 0)
        {
            _context.StockItems.Add(new StockItem
            {
                ProductId = product.Id,
                WarehouseId = primaryWarehouse.Id,
                QuantityOnHand = request.StockQuantity,
                QuantityReserved = 0,
                ReorderLevel = request.ReorderLevel
            });

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                WarehouseId = primaryWarehouse.Id,
                MovementType = StockMovementType.OpeningStock,
                QuantityChange = request.StockQuantity,
                QuantityBefore = 0,
                QuantityAfter = request.StockQuantity,
                ReferenceType = "ProductCreation",
                Reason = "Initial Opening Stock on product creation"
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            after: product,
            cancellationToken: cancellationToken);

        return await GetProductByIdAsync(product.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created product");
    }

    public async Task<ProductDetailDto> UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Product), request.Id);

        var before = new
        {
            product.Name,
            product.SKU,
            Price = product.Price.ToDecimal(),
            product.StockQuantity,
            product.IsActive
        };

        product.Name = request.Name.Trim();
        product.Slug = GenerateSlug(request.Name);
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.Price = Money.FromDecimal(request.Price);
        product.CompareAtPrice = request.CompareAtPrice.HasValue ? Money.FromDecimal(request.CompareAtPrice.Value) : null;
        product.CostPrice = Money.FromDecimal(request.CostPrice);
        product.TaxRate = request.TaxRate;
        product.DiscountType = request.DiscountType;
        product.DiscountValue = request.DiscountValue;
        product.ReorderLevel = request.ReorderLevel;
        product.MinOrderQuantity = request.MinOrderQuantity;
        product.MaxOrderQuantity = request.MaxOrderQuantity;
        product.Unit = request.Unit;
        product.WeightKg = request.WeightKg;
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.IsBestSeller = request.IsBestSeller;
        product.IsNewArrival = request.IsNewArrival;
        product.SafetyInformation = request.SafetyInformation;
        product.UpdatedAtUtc = DateTime.UtcNow;

        if (request.ImageUrls.Count > 0)
        {
            product.Images.Clear();
            int sortOrder = 0;
            foreach (var url in request.ImageUrls)
            {
                product.Images.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url = url,
                    SortOrder = sortOrder,
                    IsPrimary = sortOrder == 0
                });
                sortOrder++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            before: before,
            after: product,
            cancellationToken: cancellationToken);

        return await GetProductByIdAsync(product.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated product");
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        if (product == null) return false;

        product.IsDeleted = true;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<List<ProductDto>> GetFeaturedProductsAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => p.IsFeatured && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetBestSellersAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => p.IsBestSeller && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetNewArrivalsAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => p.IsNewArrival && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetGiftBoxesAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => (p.Category.Name.ToLower().Contains("gift box") || p.Name.ToLower().Contains("gift box")) && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetComboOffersAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => (p.Category.Name.ToLower().Contains("combo") || p.Name.ToLower().Contains("combo") || p.DiscountValue > 20) && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    private static ProductDto MapToProductDto(Product p)
    {
        var primaryImage = p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
            ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

        return new ProductDto
        {
            Id = p.Id,
            SKU = p.SKU,
            Name = p.Name,
            Slug = p.Slug,
            ShortDescription = p.ShortDescription,
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.Name ?? "Uncategorized",
            BrandId = p.BrandId,
            BrandName = p.Brand?.Name ?? "AADHI CRACKERS",
            Price = p.Price.ToDecimal(),
            CompareAtPrice = p.CompareAtPrice?.ToDecimal(),
            CostPrice = p.CostPrice.ToDecimal(),
            TaxRate = p.TaxRate,
            DiscountType = p.DiscountType,
            DiscountValue = p.DiscountValue,
            StockQuantity = p.StockQuantity,
            AvailableQuantity = p.AvailableQuantity,
            ReorderLevel = p.ReorderLevel,
            Unit = p.Unit,
            WeightKg = p.WeightKg,
            IsActive = p.IsActive,
            IsFeatured = p.IsFeatured,
            IsBestSeller = p.IsBestSeller,
            IsNewArrival = p.IsNewArrival,
            PrimaryImageUrl = primaryImage,
            Rating = 4.8,
            ReviewCount = 86
        };
    }

    private static ProductDetailDto MapToProductDetailDto(Product p, List<ProductDto> related)
    {
        var baseDto = MapToProductDto(p);

        return new ProductDetailDto
        {
            Id = baseDto.Id,
            SKU = baseDto.SKU,
            Name = baseDto.Name,
            Slug = baseDto.Slug,
            ShortDescription = baseDto.ShortDescription,
            CategoryId = baseDto.CategoryId,
            CategoryName = baseDto.CategoryName,
            BrandId = baseDto.BrandId,
            BrandName = baseDto.BrandName,
            Price = baseDto.Price,
            CompareAtPrice = baseDto.CompareAtPrice,
            CostPrice = baseDto.CostPrice,
            TaxRate = baseDto.TaxRate,
            DiscountType = baseDto.DiscountType,
            DiscountValue = baseDto.DiscountValue,
            StockQuantity = baseDto.StockQuantity,
            AvailableQuantity = baseDto.AvailableQuantity,
            ReorderLevel = baseDto.ReorderLevel,
            Unit = baseDto.Unit,
            WeightKg = baseDto.WeightKg,
            IsActive = baseDto.IsActive,
            IsFeatured = baseDto.IsFeatured,
            IsBestSeller = baseDto.IsBestSeller,
            IsNewArrival = baseDto.IsNewArrival,
            PrimaryImageUrl = baseDto.PrimaryImageUrl,
            Rating = baseDto.Rating,
            ReviewCount = baseDto.ReviewCount,
            Description = p.Description,
            SafetyInformation = p.SafetyInformation ?? "1. Keep water and sand nearby.\n2. Maintain minimum 5 meters distance.\n3. Light with extended incense stick/agarbathi.\n4. Children must be supervised by adults.",
            MinOrderQuantity = p.MinOrderQuantity,
            MaxOrderQuantity = p.MaxOrderQuantity,
            Images = p.Images.OrderBy(i => i.SortOrder).Select(i => new ProductImageDto
            {
                Id = i.Id,
                Url = i.Url,
                AltText = i.AltText,
                SortOrder = i.SortOrder,
                IsPrimary = i.IsPrimary
            }).ToList(),
            RelatedProducts = related
        };
    }

    private static string GenerateSlug(string text)
    {
        var clean = text.ToLowerInvariant().Trim();
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^a-z0-9\s-]", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", "-").Trim('-');
        return clean;
    }
}
