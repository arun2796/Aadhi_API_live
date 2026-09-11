using AadhiCrackers.Application.Common;
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
    Task<bool> DeleteBrandAsync(Guid id, CancellationToken cancellationToken = default);

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
    Task<List<ProductDto>> GetLowStockProductsAsync(int count = 10, CancellationToken cancellationToken = default);
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
            SeoTitle = c.SeoTitle,
            SeoDescription = c.SeoDescription,
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
        if (request.ParentCategoryId.HasValue)
        {
            var parentExists = await _context.Categories.AnyAsync(c => c.Id == request.ParentCategoryId.Value && !c.IsDeleted, cancellationToken);
            if (!parentExists)
                throw new ResourceNotFoundException(nameof(Category), request.ParentCategoryId.Value);
        }

        var (slug, isCustomSlug) = ResolveCategorySlug(request.Slug, request.Name);
        var slugTaken = await _context.Categories
            .AnyAsync(c => c.Slug.ToLower() == slug.ToLower() && !c.IsDeleted, cancellationToken);
        if (slugTaken)
            throw new DomainException(isCustomSlug
                ? $"A category with the slug '{slug}' already exists. Please choose a different slug."
                : $"A category named '{request.Name.Trim()}' already exists. Please choose a different name.");

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            ImageUrl = ImageUrlNormalizer.Normalize(request.ImageUrl),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription
        };

        _context.Categories.Add(category);
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            after: category,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

        if (request.ParentCategoryId == category.Id)
            throw new DomainException("Category cannot be its own parent.");

        if (request.ParentCategoryId.HasValue)
        {
            // Check for circular dependency
            var currentParentId = request.ParentCategoryId;
            while (currentParentId.HasValue)
            {
                if (currentParentId.Value == category.Id)
                    throw new DomainException("Circular parent category relationship is not allowed.");

                var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentParentId.Value && !c.IsDeleted, cancellationToken);
                currentParentId = parent?.ParentCategoryId;
            }
        }

        var before = new { category.Name, category.Slug, category.Description, category.IsActive };

        var (updatedSlug, isCustomUpdatedSlug) = ResolveCategorySlug(request.Slug, request.Name);
        var updatedSlugTaken = await _context.Categories
            .AnyAsync(c => c.Id != category.Id && c.Slug.ToLower() == updatedSlug.ToLower() && !c.IsDeleted, cancellationToken);
        if (updatedSlugTaken)
            throw new DomainException(isCustomUpdatedSlug
                ? $"Another category with the slug '{updatedSlug}' already exists. Please choose a different slug."
                : $"Another category named '{request.Name.Trim()}' already exists. Please choose a different name.");

        category.Name = request.Name.Trim();
        category.Slug = updatedSlug;
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.ImageUrl = ImageUrlNormalizer.Normalize(request.ImageUrl);
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.SeoTitle = request.SeoTitle;
        category.SeoDescription = request.SeoDescription;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            before: before,
            after: category,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted, cancellationToken);
        if (hasProducts)
            throw new DomainException("Cannot delete category because it contains active products. Reassign or delete products first.");

        var hasSubcategories = await _context.Categories.AnyAsync(c => c.ParentCategoryId == id && !c.IsDeleted, cancellationToken);
        if (hasSubcategories)
            throw new DomainException("Cannot delete category because it has subcategories. Reassign or delete subcategories first.");

        category.IsDeleted = true;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Brand),
            brand.Id.ToString(),
            brand.Name,
            after: brand,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BrandDto
        {
            Id = brand.Id,
            Name = brand.Name,
            Slug = brand.Slug,
            Description = brand.Description,
            LogoUrl = brand.LogoUrl,
            IsActive = brand.IsActive,
            ProductCount = 0
        };
    }

    public async Task<bool> DeleteBrandAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Brand), id);

        brand.IsDeleted = true;
        brand.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);
        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(Brand),
            brand.Id.ToString(),
            brand.Name,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(ProductFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.ProductCategories)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
            .Where(p => !p.IsDeleted);

        if (filter.ProductType.HasValue)
            query = query.Where(p => p.ProductType == filter.ProductType.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || p.SKU.ToLower().Contains(s) || p.Description.ToLower().Contains(s));
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value || p.ProductCategories.Any(pc => pc.CategoryId == filter.CategoryId.Value));
        }
        else if (!string.IsNullOrWhiteSpace(filter.CategorySlug))
        {
            query = query.Where(p => p.Category.Slug.ToLower() == filter.CategorySlug.ToLower() || p.ProductCategories.Any(pc => pc.Category.Slug.ToLower() == filter.CategorySlug.ToLower()));
        }

        if (filter.BrandId.HasValue)
            query = query.Where(p => p.BrandId == filter.BrandId.Value);

        if (filter.MinPrice.HasValue)
        {
            var minPrice = Money.FromDecimal(filter.MinPrice.Value);
            query = query.Where(p => p.Price >= minPrice);
        }

        if (filter.MaxPrice.HasValue)
        {
            var maxPrice = Money.FromDecimal(filter.MaxPrice.Value);
            query = query.Where(p => p.Price <= maxPrice);
        }

        if (filter.InStockOnly == true)
            query = query.Where(p => p.StockQuantity > p.ReservedQuantity);

        if (filter.IsFeatured == true)
            query = query.Where(p => p.IsFeatured);

        if (filter.IsBestSeller == true)
            query = query.Where(p => p.IsBestSeller);

        if (filter.IsNewArrival == true)
            query = query.Where(p => p.IsNewArrival);

        if (filter.ExcludeCombos == true)
            query = query.Where(p => !p.ComboItems.Any());

        // Applied here, before the sort / CountAsync / paging, so totalCount reflects it and the
        // storefront can show gift boxes in their own section instead of inside category listings.
        if (filter.ExcludeGiftBoxes == true)
            query = query.Where(p => !p.IsGiftBox);

        query = filter.SortBy switch
        {

            "price_asc" => query.OrderBy(p => EF.Property<long>(p, nameof(Product.Price))),
            "price_desc" => query.OrderByDescending(p => EF.Property<long>(p, nameof(Product.Price))),
            "new" => query.OrderByDescending(p => p.CreatedAtUtc),
            "name_asc" => query.OrderBy(p => p.Name),
            _ => query.OrderByDescending(p => p.IsFeatured).ThenByDescending(p => p.IsBestSeller).ThenBy(p => p.Name)
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
        var product = await ProductDetailQuery()
            .FirstOrDefaultAsync(p => p.Slug.ToLower() == slug.ToLower() && !p.IsDeleted, cancellationToken);

        if (product == null) return null;

        var related = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id && p.IsActive && !p.IsDeleted)
            .Take(4)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

        return MapToProductDetailDto(product, related);
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await ProductDetailQuery()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (product == null) return null;

        return MapToProductDetailDto(product, new List<ProductDto>());
    }

    /// <summary>
    /// The full graph a product detail response needs. Combo components are loaded (with their
    /// images) so ComboItemsTotal can be summed from their CURRENT prices.
    /// </summary>
    private IQueryable<Product> ProductDetailQuery() =>
        _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.ProductCategories)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems.OrderBy(ci => ci.SortOrder))
                .ThenInclude(ci => ci.ComponentProduct)
                    .ThenInclude(cp => cp.Images);

    public async Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var slug = GenerateSlug(request.Name);

        if (await _context.Products.AnyAsync(p => p.SKU.ToLower() == request.SKU.ToLower() && !p.IsDeleted, cancellationToken))
        {
            throw new DomainException($"Product with SKU '{request.SKU}' already exists.");
        }

        if (await _context.Products.AnyAsync(p => p.Slug.ToLower() == slug.ToLower() && !p.IsDeleted, cancellationToken))
        {
            throw new DomainException($"A product named '{request.Name.Trim()}' already exists. Please choose a different name.");
        }

        var product = new Product
        {
            SKU = request.SKU.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            ProductType = request.ProductType,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Price = Money.FromDecimal(request.Price),
            CompareAtPrice = request.CompareAtPrice.HasValue ? Money.FromDecimal(request.CompareAtPrice.Value) : null,
            CostPrice = Money.FromDecimal(request.CostPrice),
            TaxRate = request.TaxRate > 0 ? request.TaxRate : 0m,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            StockQuantity = request.StockQuantity > 0 ? request.StockQuantity : 9999,
            ReorderLevel = request.ReorderLevel > 0 ? request.ReorderLevel : 10,
            MinOrderQuantity = request.MinOrderQuantity > 0 ? request.MinOrderQuantity : 1,
            MaxOrderQuantity = request.MaxOrderQuantity,
            Unit = request.Unit,
            WeightKg = request.WeightKg,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            IsBestSeller = request.IsBestSeller,
            IsNewArrival = request.IsNewArrival,
            IsGiftBox = request.IsGiftBox,
            SafetyInformation = request.SafetyInformation
        };

        // Primary Category Link
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = request.CategoryId,
            IsPrimary = true
        });

        if (request.AdditionalCategoryIds != null)
        {
            foreach (var catId in request.AdditionalCategoryIds.Where(id => id != request.CategoryId).Distinct())
            {
                product.ProductCategories.Add(new ProductCategory
                {
                    ProductId = product.Id,
                    CategoryId = catId,
                    IsPrimary = false
                });
            }
        }

        int sortOrder = 0;
        foreach (var url in request.ImageUrls)
        {
            product.Images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = ImageUrlNormalizer.Normalize(url) ?? url,
                SortOrder = sortOrder,
                IsPrimary = sortOrder == 0
            });
            sortOrder++;
        }

        // Combo / gift-box composition (also flips ProductType to Bundle when non-empty).
        // CREATE has no "leave it as it is" state — there is nothing to preserve yet — so an
        // absent list is normalised to empty and simply means "this is a plain product".
        await ApplyComboItemsAsync(product, request.ComboItems ?? new List<CreateComboItemRequest>(), cancellationToken);

        _context.Products.Add(product);


        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            after: product,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetProductByIdAsync(product.Id, cancellationToken))!;
    }

    public async Task<ProductDetailDto> UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .Include(p => p.ProductCategories)
            .Include(p => p.ComboItems)
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

        var updatedProductSlug = GenerateSlug(request.Name);
        var productSlugTaken = await _context.Products
            .AnyAsync(p => p.Id != product.Id && p.Slug.ToLower() == updatedProductSlug.ToLower() && !p.IsDeleted, cancellationToken);
        if (productSlugTaken)
            throw new DomainException($"Another product named '{request.Name.Trim()}' already exists. Please choose a different name.");

        product.Name = request.Name.Trim();
        product.Slug = updatedProductSlug;
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        // A request that does not send the combo list cannot be trusted to know that this product
        // is a combo either, so a product that keeps its composition also keeps its Bundle type —
        // the same invariant ApplyComboItemsAsync enforces whenever a non-empty list is applied.
        product.ProductType = request.ComboItems is null && product.ComboItems.Count > 0
            ? ProductType.Bundle
            : request.ProductType;
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
        product.IsGiftBox = request.IsGiftBox;
        product.SafetyInformation = request.SafetyInformation;
        product.UpdatedAtUtc = DateTime.UtcNow;

        // Categories Sync
        product.ProductCategories.Clear();
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = request.CategoryId,
            IsPrimary = true
        });

        if (request.AdditionalCategoryIds != null)
        {
            foreach (var catId in request.AdditionalCategoryIds.Where(id => id != request.CategoryId).Distinct())
            {
                product.ProductCategories.Add(new ProductCategory
                {
                    ProductId = product.Id,
                    CategoryId = catId,
                    IsPrimary = false
                });
            }
        }

        // The image list is authoritative: the admin product form always submits the
        // complete gallery, so an empty list means "this product has no images" and
        // must clear them (previously an empty list was ignored, making it impossible
        // to remove the last image).
        product.Images.Clear();
        int sortOrder = 0;
        foreach (var url in request.ImageUrls)
        {
            product.Images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = ImageUrlNormalizer.Normalize(url) ?? url,
                SortOrder = sortOrder,
                IsPrimary = sortOrder == 0
            });
            sortOrder++;
        }

        // Unlike the image list, the combo list is only authoritative when it is actually sent:
        // null (field omitted) leaves the stored composition alone, [] clears it and drops the
        // product back to Simple, non-empty replaces it. See UpdateProductRequest.ComboItems.
        await ApplyComboItemsAsync(product, request.ComboItems, cancellationToken);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            before: before,
            after: product,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetProductByIdAsync(product.Id, cancellationToken))!;
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        if (product == null) return false;

        product.IsDeleted = true;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Catalog",
            nameof(Product),
            product.Id.ToString(),
            product.Name,
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<List<ProductDto>> GetFeaturedProductsAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
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
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
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
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
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
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
            // Exactly what the admin has flagged — nothing is inferred from the name, the category
            // or the product type. A gift box is a sealed SKU, so unlike GetComboOffersAsync below
            // there is no composition to rank on and the flag alone decides membership.
            .Where(p => p.IsGiftBox && p.IsActive && !p.IsDeleted)
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetComboOffersAsync(int count = 8, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
            // Real combos (products that actually have components) come first, then the
            // historical name/category/discount matching so nothing that used to appear drops off.
            .Where(p => (p.ComboItems.Any() || p.Category.Name.ToLower().Contains("combo") || p.Name.ToLower().Contains("combo") || p.DiscountValue > 20) && p.IsActive && !p.IsDeleted)
            .OrderByDescending(p => p.ComboItems.Any())
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    public async Task<List<ProductDto>> GetLowStockProductsAsync(int count = 10, CancellationToken cancellationToken = default) =>
        await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.ComboItems)
            .Where(p => p.IsActive && !p.IsDeleted && (p.StockQuantity - p.ReservedQuantity) <= p.ReorderLevel)
            .OrderBy(p => (p.StockQuantity - p.ReservedQuantity))
            .Take(count)
            .Select(p => MapToProductDto(p))
            .ToListAsync(cancellationToken);

    private static ProductDto MapToProductDto(Product p)
    {
        var primaryImage = p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
            ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url;

        // Calculate real rating from approved reviews
        var approvedReviews = p.Reviews.Where(r => r.Status == "Approved" && !r.IsDeleted).ToList();
        var rating = approvedReviews.Any() ? Math.Round(approvedReviews.Average(r => r.Rating), 1) : 0.0;
        var reviewCount = approvedReviews.Count;

        // A real combo is one that HAS components, regardless of its name or category.
        var comboItemCount = p.ComboItems.Count(ci => !ci.IsDeleted);

        return new ProductDto
        {
            Id = p.Id,
            SKU = p.SKU,
            Name = p.Name,
            Slug = p.Slug,
            ShortDescription = p.ShortDescription,
            ProductType = p.ProductType,
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.Name ?? "Uncategorized",
            CategoryIds = p.ProductCategories.Select(pc => pc.CategoryId).ToList(),
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
            IsGiftBox = p.IsGiftBox,
            PrimaryImageUrl = primaryImage,
            Rating = rating,
            ReviewCount = reviewCount,
            IsCombo = comboItemCount > 0,
            ComboItemCount = comboItemCount
        };
    }

    private static List<ProductComboItem> LiveComboItems(Product p) =>
        p.ComboItems.Where(ci => !ci.IsDeleted).OrderBy(ci => ci.SortOrder).ToList();

    private static ComboItemDto MapToComboItemDto(ProductComboItem ci)
    {
        var component = ci.ComponentProduct;
        var unitPrice = component.Price.ToDecimal();

        return new ComboItemDto
        {
            ComponentProductId = ci.ComponentProductId,
            ProductName = component.Name,
            Sku = component.SKU,
            ImageUrl = component.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Url
                ?? component.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url,
            Quantity = ci.Quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * ci.Quantity
        };
    }

    private static ProductDetailDto MapToProductDetailDto(Product p, List<ProductDto> related)
    {
        var baseDto = MapToProductDto(p);

        // A soft-deleted component is filtered out by the Product query filter and arrives as
        // null; skip those lines rather than blowing up on an orphaned composition row.
        var comboItems = LiveComboItems(p)
            .Where(ci => ci.ComponentProduct != null)
            .Select(MapToComboItemDto)
            .ToList();

        return new ProductDetailDto
        {
            ComboItems = comboItems,
            ComboItemsTotal = comboItems.Sum(ci => ci.LineTotal),
            IsCombo = comboItems.Count > 0,
            ComboItemCount = comboItems.Count,
            Id = baseDto.Id,
            SKU = baseDto.SKU,
            Name = baseDto.Name,
            Slug = baseDto.Slug,
            ShortDescription = baseDto.ShortDescription,
            ProductType = baseDto.ProductType,
            CategoryId = baseDto.CategoryId,
            CategoryName = baseDto.CategoryName,
            CategoryIds = baseDto.CategoryIds,
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
            IsGiftBox = baseDto.IsGiftBox,
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

    /// <summary>
    /// Applies the product's combo / gift-box composition. The requested list is three-state:
    /// <c>null</c> leaves the stored composition completely untouched, an empty list clears it and
    /// drops the product back to Simple, and a non-empty one replaces it and marks the product as a
    /// Bundle. Rows that survive the edit are updated in place so the unique
    /// (ComboProductId, ComponentProductId) index is never transiently violated.
    ///
    /// The selling price is NEVER touched here: Price and CompareAtPrice stay exactly as the owner
    /// typed them. Only ProductDetailDto.ComboItemsTotal exposes what the parts are worth.
    ///
    /// This is also the single place that enforces "a product is never both a combo and a gift
    /// box", because it is the only point on both the create and the update path that knows the
    /// composition the request ends up with.
    /// </summary>
    private async Task ApplyComboItemsAsync(
        Product product,
        List<CreateComboItemRequest>? requestedItems,
        CancellationToken cancellationToken)
    {
        // A combo and a gift box are mutually exclusive (see Product.IsGiftBox). "Combo" is not a
        // stored flag but a derived fact — having components — so the check has to look at the
        // composition this request LEAVES BEHIND, which for the null (field omitted) case is
        // whatever is already stored. Both callers set IsGiftBox from the request before getting
        // here, so product.IsGiftBox is already the requested value.
        var willHaveComboItems = requestedItems is null
            ? product.ComboItems.Any(ci => !ci.IsDeleted)
            : requestedItems.Count > 0;

        if (product.IsGiftBox && willHaveComboItems)
        {
            throw new DomainException(
                $"'{product.Name}' cannot be a combo and a gift box at the same time. A gift box is sold as one sealed box without listing what is inside, so either clear its combo items or untick 'Gift Box'.");
        }

        // Three-state contract (see UpdateProductRequest.ComboItems):
        //   null       -> the caller did not send the field: leave the composition EXACTLY as it is.
        //   empty list -> the caller cleared it: drop every item, back to a simple product.
        //   non-empty  -> replace the composition with these lines.
        // The null case must return before touching anything, otherwise an ordinary product edit
        // that simply omits "comboItems" would wipe a real combo. Only UPDATE can reach it; the
        // create path normalises its (never-null) list before calling in.
        if (requestedItems is null)
            return;

        var requested = requestedItems;

        foreach (var item in requested)
        {
            if (item.ComponentProductId == Guid.Empty)
                throw new DomainException("Every combo item must point at a product. Please pick a product for each row.");

            if (item.Quantity < 1)
                throw new DomainException("Each combo item needs a quantity of at least 1.");

            if (item.ComponentProductId == product.Id)
                throw new DomainException($"'{product.Name}' cannot be an item inside itself. Remove it from the combo list.");
        }

        var duplicate = requested
            .GroupBy(i => i.ComponentProductId)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new DomainException("The same product is listed twice in this combo. Increase that item's quantity instead of adding it again.");

        var existing = product.ComboItems.ToList();

        if (requested.Count == 0)
        {
            foreach (var stale in existing)
            {
                product.ComboItems.Remove(stale);
            }

            if (product.ProductType == ProductType.Bundle)
            {
                product.ProductType = ProductType.Simple;
            }
            return;
        }

        // A combo may not be built out of other combos, and a product that is already used as a
        // component may not itself become one — either way that would nest compositions.
        var alreadyAComponent = await _context.ProductComboItems
            .AnyAsync(ci => ci.ComponentProductId == product.Id, cancellationToken);
        if (alreadyAComponent)
            throw new DomainException($"'{product.Name}' is already used as an item inside another combo, so it cannot be turned into a combo itself.");

        var componentIds = requested.Select(i => i.ComponentProductId).ToList();
        var components = await _context.Products
            .AsNoTracking()
            .Where(p => componentIds.Contains(p.Id) && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.IsActive, IsItselfACombo = p.ComboItems.Any() })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var sortOrder = 0;
        foreach (var item in requested)
        {
            if (!components.TryGetValue(item.ComponentProductId, out var component))
                throw new DomainException($"Combo item product '{item.ComponentProductId}' does not exist. Refresh the product list and try again.");

            if (!component.IsActive)
                throw new DomainException($"'{component.Name}' is inactive and cannot be added to a combo. Activate it first or remove it from the list.");

            if (component.IsItselfACombo)
                throw new DomainException($"'{component.Name}' is itself a combo. A combo cannot contain another combo — add its individual products instead.");

            var match = existing.FirstOrDefault(e => e.ComponentProductId == item.ComponentProductId);
            if (match != null)
            {
                match.Quantity = item.Quantity;
                match.SortOrder = sortOrder;
            }
            else
            {
                product.ComboItems.Add(new ProductComboItem
                {
                    ComboProductId = product.Id,
                    ComponentProductId = item.ComponentProductId,
                    Quantity = item.Quantity,
                    SortOrder = sortOrder
                });
            }

            sortOrder++;
        }

        foreach (var stale in existing.Where(e => !componentIds.Contains(e.ComponentProductId)).ToList())
        {
            product.ComboItems.Remove(stale);
        }

        product.ProductType = ProductType.Bundle;
    }

    private static string GenerateSlug(string text)
    {
        var clean = text.ToLowerInvariant().Trim();
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^a-z0-9\s-]", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", "-").Trim('-');
        return clean;
    }

    /// <summary>
    /// Uses the provided custom slug (normalized) when non-empty, otherwise derives the slug from the name.
    /// Returns the resolved slug and whether a usable custom slug was supplied.
    /// </summary>
    private static (string Slug, bool IsCustom) ResolveCategorySlug(string? customSlug, string name)
    {
        if (!string.IsNullOrWhiteSpace(customSlug))
        {
            var normalized = GenerateSlug(customSlug);
            if (normalized.Length > 0)
                return (normalized, true);
        }

        return (GenerateSlug(name), false);
    }
}
