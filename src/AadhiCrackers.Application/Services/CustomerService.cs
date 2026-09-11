using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Customers;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> GetCustomersAsync(int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
}

public class CustomerService : ICustomerService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;

    public CustomerService(IApplicationDbContext context, IAuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    public async Task<CustomerDto?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsDeleted, cancellationToken);

        return customer == null ? null : MapToDto(customer);
    }

    public async Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email.ToLower() == email.ToLower() && !c.IsDeleted, cancellationToken);

        return customer == null ? null : MapToDto(customer);
    }

    public async Task<PagedResult<CustomerDto>> GetCustomersAsync(int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Where(c => !c.IsDeleted)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s) ||
                c.Phone.Contains(s) ||
                c.CustomerCode.ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var customers = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = customers.Select(c => MapToDto(c)).ToList();
        return new PagedResult<CustomerDto>(items, totalCount, page, pageSize);
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);

        return customer == null ? null : MapToDto(customer);
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var phone = request.Phone.Trim();
        var email = request.Email?.Trim();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new DomainException("First name is required.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\d{10}$"))
        {
            throw new DomainException("Enter a valid 10-digit phone number.");
        }

        if (!string.IsNullOrWhiteSpace(email) &&
            !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            throw new DomainException("A valid email address is required.");
        }

        var phoneExists = await _context.Customers.AnyAsync(c => c.Phone == phone && !c.IsDeleted, cancellationToken);
        if (phoneExists)
        {
            throw new DomainException($"A customer with phone number '{phone}' already exists.");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailLower = email.ToLower();
            var emailExists = await _context.Customers.AnyAsync(c => c.Email.ToLower() == emailLower && !c.IsDeleted, cancellationToken);
            if (emailExists)
            {
                throw new DomainException($"A customer with email '{email}' already exists.");
            }
        }

        var count = await _context.Customers.CountAsync(cancellationToken) + 1;
        var customerCode = $"CUST-{DateTime.UtcNow:yyMM}-{count:D4}";

        var customer = new Customer
        {
            CustomerCode = customerCode,
            FirstName = firstName,
            LastName = lastName,
            // Email column has a unique index and is non-nullable — synthesize a placeholder
            // (same convention as the Firebase auto-provision flow) when none is provided.
            Email = !string.IsNullOrWhiteSpace(email) ? email : $"{customerCode.ToLower()}@customer.aadhicracker.in",
            Phone = phone,
            IsActive = true
        };

        _context.Customers.Add(customer);

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Customers",
            nameof(Customer),
            customer.Id.ToString(),
            customer.FullName,
            after: new { customer.CustomerCode, customer.FirstName, customer.LastName, customer.Email, customer.Phone, customer.IsActive },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(customer);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Customer), id);

        var before = new { customer.FirstName, customer.LastName, customer.Phone, customer.IsActive };

        customer.FirstName = request.FirstName.Trim();
        customer.LastName = request.LastName.Trim();
        customer.Phone = request.Phone.Trim();
        customer.IsActive = request.IsActive;
        customer.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Customers",
            nameof(Customer),
            customer.Id.ToString(),
            customer.FullName,
            before: before,
            after: new { customer.FirstName, customer.LastName, customer.Phone, customer.IsActive },
            cancellationToken: cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(customer);
    }

    private static CustomerDto MapToDto(Customer c)
    {
        var nonCancelledOrders = c.Orders?.Where(o => o != null && o.OrderStatus != OrderStatus.Cancelled && !o.IsDeleted).ToList() ?? new List<Order>();
        return new CustomerDto
        {
            Id = c.Id,
            CustomerCode = c.CustomerCode ?? string.Empty,
            FirstName = c.FirstName ?? string.Empty,
            LastName = c.LastName ?? string.Empty,
            Email = c.Email ?? string.Empty,
            Phone = c.Phone ?? string.Empty,
            DateOfBirth = c.DateOfBirth,
            IsActive = c.IsActive,
            RewardPoints = c.RewardPoints,
            TotalOrders = nonCancelledOrders.Count,
            TotalSpent = nonCancelledOrders.Sum(o => o.GrandTotal != null ? o.GrandTotal.ToDecimal() : 0m),
            CreatedAtUtc = c.CreatedAtUtc,
            Addresses = (c.Addresses ?? Enumerable.Empty<CustomerAddress>())
                .Where(a => a != null)
                .Select(a => new CustomerAddressDto
                {
                    Id = a.Id,
                    AddressType = a.AddressType,
                    FullName = a.Address?.FullName ?? string.Empty,
                    Phone = a.Address?.Phone ?? string.Empty,
                    AddressLine1 = a.Address?.AddressLine1 ?? string.Empty,
                    AddressLine2 = a.Address?.AddressLine2,
                    City = a.Address?.City ?? string.Empty,
                    State = a.Address?.State ?? string.Empty,
                    PostalCode = a.Address?.PostalCode ?? string.Empty,
                    Country = a.Address?.Country ?? string.Empty,
                    IsDefault = a.IsDefault
                }).ToList()
        };
    }
}
