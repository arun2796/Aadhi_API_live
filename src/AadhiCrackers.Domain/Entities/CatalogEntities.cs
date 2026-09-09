using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class Category : BaseEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
}

public class Brand : BaseEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class ProductImage : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class Product : AggregateRoot<Guid>
{
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public ProductType ProductType { get; set; } = ProductType.Simple;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public Money Price { get; set; } = Money.Zero();
    public Money? CompareAtPrice { get; set; }
    public Money CostPrice { get; set; } = Money.Zero();
    public decimal TaxRate { get; set; } = 18.00m; // Standard 18% GST for fireworks
    public DiscountType DiscountType { get; set; } = DiscountType.None;
    public decimal DiscountValue { get; set; }
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int ReorderLevel { get; set; } = 20;
    public int MinOrderQuantity { get; set; } = 1;
    public int MaxOrderQuantity { get; set; } = 100;
    public string Unit { get; set; } = "Box"; // Box, Pack, Piece
    public decimal WeightKg { get; set; } = 0.5m;
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public bool IsBestSeller { get; set; }
    public bool IsNewArrival { get; set; }
    public string? SafetyInformation { get; set; }
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();

    /// <summary>
    /// The products this one is composed of when it is a combo / gift box. Empty for a normal
    /// product. A product that has combo items may not itself be used as a component of another
    /// combo (no nesting).
    /// </summary>
    public ICollection<ProductComboItem> ComboItems { get; set; } = new List<ProductComboItem>();

    public int AvailableQuantity => Math.Max(0, StockQuantity - ReservedQuantity);
}

/// <summary>
/// One line of a combo / gift-box composition: "this combo contains N of that product".
/// The combo's selling price stays fully manual (Product.Price) — the summed component value is
/// only computed for display so the owner can decide what to charge and what to strike through.
/// </summary>
public class ProductComboItem : BaseEntity<Guid>
{
    /// <summary>The combo / gift box being composed.</summary>
    public Guid ComboProductId { get; set; }
    public Product ComboProduct { get; set; } = null!;

    /// <summary>A product included in the combo.</summary>
    public Guid ComponentProductId { get; set; }
    public Product ComponentProduct { get; set; } = null!;

    /// <summary>How many of the component the combo contains. Always at least 1.</summary>
    public int Quantity { get; set; } = 1;

    public int SortOrder { get; set; }
}

public class ProductCategory : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public bool IsPrimary { get; set; }
}

public class ProductReview : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Approved"; // Pending, Approved, Rejected, Hidden
}
