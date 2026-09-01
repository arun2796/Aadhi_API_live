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
        var nonCancelledOrders = c.Orders.Where(o => o.OrderStatus != OrderStatus.Cancelled && !o.IsDeleted).ToList();
        return new CustomerDto
        {
            Id = c.Id,
            CustomerCode = c.CustomerCode,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = c.Email,
            Phone = c.Phone,
            DateOfBirth = c.DateOfBirth,
            IsActive = c.IsActive,
            TotalOrders = nonCancelledOrders.Count,
            TotalSpent = nonCancelledOrders.Sum(o => o.GrandTotal.ToDecimal()),
            CreatedAtUtc = c.CreatedAtUtc,
            Addresses = c.Addresses.Select(a => new CustomerAddressDto
            {
                Id = a.Id,
                AddressType = a.AddressType,
                FullName = a.Address.FullName,
                Phone = a.Address.Phone,
                AddressLine1 = a.Address.AddressLine1,
                AddressLine2 = a.Address.AddressLine2,
                City = a.Address.City,
                State = a.Address.State,
                PostalCode = a.Address.PostalCode,
                Country = a.Address.Country,
                IsDefault = a.IsDefault
            }).ToList()
        };
    }
}
