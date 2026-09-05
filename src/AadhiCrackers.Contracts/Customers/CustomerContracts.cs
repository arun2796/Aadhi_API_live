using AadhiCrackers.Contracts.Orders;

namespace AadhiCrackers.Contracts.Customers;

public class CustomerDto
{
    public Guid Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public bool IsActive { get; set; } = true;
    public int RewardPoints { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<CustomerAddressDto> Addresses { get; set; } = new();
}

public class CreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
}

public class UpdateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

// ---- Customer self-service addresses (§3) ----

public class AddressDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = "Home"; // Home | Office | Other
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

// ---- Wishlist (§2) ----

public class WishlistProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? Mrp { get; set; }
    public int DiscountPercent { get; set; }
    public string? ImageUrl { get; set; }
    public bool InStock { get; set; }
}

public class WishlistItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public WishlistProductDto Product { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
