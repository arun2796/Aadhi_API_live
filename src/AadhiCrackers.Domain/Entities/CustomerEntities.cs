using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;

namespace AadhiCrackers.Domain.Entities;

public class CustomerAddress : BaseEntity<Guid>
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public AddressType AddressType { get; set; } = AddressType.Both;
    public Address Address { get; set; } = new();
    public bool IsDefault { get; set; }
}

public class Customer : AggregateRoot<Guid>
{
    public string UserId { get; set; } = string.Empty; // ASP.NET Identity User ID
    public string CustomerCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public bool IsActive { get; set; } = true;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
