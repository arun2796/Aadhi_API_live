using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Contracts.Catalog;

public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = new();
}

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional custom URL slug. When empty, the slug is derived from Name.</summary>
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
}

public class UpdateCategoryRequest : CreateCategoryRequest
{
    public Guid Id { get; set; }
}

public class BrandDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
}

public class CreateBrandRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductImageDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class ProductDto
{
    public Guid Id { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public ProductType ProductType { get; set; } = ProductType.Simple;
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<Guid> CategoryIds { get; set; } = new();
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TaxRate { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public int DiscountPercentage => CompareAtPrice > Price && CompareAtPrice > 0
        ? (int)Math.Round((1 - (Price / CompareAtPrice.Value)) * 100)
        : 0;
    public int StockQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public string Unit { get; set; } = "Box";
    public decimal WeightKg { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsBestSeller { get; set; }
    public bool IsNewArrival { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public double Rating { get; set; } = 4.8;
    public int ReviewCount { get; set; } = 86;
}

public class ProductDetailDto : ProductDto
{
    public string Description { get; set; } = string.Empty;
    public string? SafetyInformation { get; set; }
    public int MinOrderQuantity { get; set; }
    public int MaxOrderQuantity { get; set; }
    public List<ProductImageDto> Images { get; set; } = new();
    public List<ProductDto> RelatedProducts { get; set; } = new();
}

public class ProductFilterRequest
{
    public string? Search { get; set; }
    public ProductType? ProductType { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategorySlug { get; set; }
    public Guid? BrandId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? InStockOnly { get; set; }
    public bool? IsFeatured { get; set; }
    public bool? IsBestSeller { get; set; }
    public bool? IsNewArrival { get; set; }
    public string? SortBy { get; set; } // price_asc, price_desc, popularity, new, name_asc
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CreateProductRequest
{
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public ProductType ProductType { get; set; } = ProductType.Simple;
    public Guid CategoryId { get; set; }
    public List<Guid>? AdditionalCategoryIds { get; set; }
    public Guid? BrandId { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TaxRate { get; set; } = 18m;
    public DiscountType DiscountType { get; set; } = DiscountType.None;
    public decimal DiscountValue { get; set; }
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; } = 20;
    public int MinOrderQuantity { get; set; } = 1;
    public int MaxOrderQuantity { get; set; } = 100;
    public string Unit { get; set; } = "Box";
    public decimal WeightKg { get; set; } = 0.5m;
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public bool IsBestSeller { get; set; }
    public bool IsNewArrival { get; set; }
    public string? SafetyInformation { get; set; }
    public List<string> ImageUrls { get; set; } = new();
}

public class UpdateProductRequest : CreateProductRequest
{
    public Guid Id { get; set; }
}
