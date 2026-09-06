using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Enquiries;
using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Application.Services;

public interface IEnquiryService
{
    Task<PagedResult<EnquiryDto>> GetEnquiriesAsync(int page = 1, int pageSize = 20, EnquiryStatus? status = null, EnquirySource? source = null, string? search = null, CancellationToken cancellationToken = default);
    Task<EnquiryDto?> GetEnquiryByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EnquiryDto> CreateEnquiryAsync(CreateEnquiryRequest request, CancellationToken cancellationToken = default);
    Task<EnquiryDto> UpdateEnquiryAsync(Guid id, UpdateEnquiryRequest request, CancellationToken cancellationToken = default);
    Task<EnquiryDto> UpdateEnquiryStatusAsync(Guid id, UpdateEnquiryStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteEnquiryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<EnquiryCustomerDto>> GetEnquiryCustomersAsync(CancellationToken cancellationToken = default);
}

public class EnquiryService : IEnquiryService
{
    private readonly IApplicationDbContext _context;
    private readonly IAuditLogService _auditLog;
    private readonly IBusinessNumberGenerator _numberGenerator;

    public EnquiryService(
        IApplicationDbContext context,
        IAuditLogService auditLog,
        IBusinessNumberGenerator numberGenerator)
    {
        _context = context;
        _auditLog = auditLog;
        _numberGenerator = numberGenerator;
    }

    public async Task<PagedResult<EnquiryDto>> GetEnquiriesAsync(int page = 1, int pageSize = 20, EnquiryStatus? status = null, EnquirySource? source = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Enquiries
            .AsNoTracking()
            .Include(e => e.Items)
            .Where(e => !e.IsDeleted);

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        if (source.HasValue)
        {
            query = query.Where(e => e.Source == source.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(e =>
                e.EnquiryNumber.ToLower().Contains(s) ||
                e.CustomerName.ToLower().Contains(s) ||
                e.Phone.Contains(s));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var enquiries = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = enquiries.Select(MapToDto).ToList();
        return new PagedResult<EnquiryDto>(items, totalCount, page, pageSize);
    }

    public async Task<EnquiryDto?> GetEnquiryByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var enquiry = await _context.Enquiries
            .AsNoTracking()
            .Include(e => e.Items)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken);

        return enquiry == null ? null : MapToDto(enquiry);
    }

    public async Task<EnquiryDto> CreateEnquiryAsync(CreateEnquiryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new DomainException("Enquiry must contain at least one item.");
        }

        if (request.CustomerId.HasValue)
        {
            var customerExists = await _context.Customers
                .AnyAsync(c => c.Id == request.CustomerId.Value && !c.IsDeleted, cancellationToken);
            if (!customerExists)
            {
                throw new ResourceNotFoundException(nameof(Customer), request.CustomerId.Value);
            }
        }

        var enquiryNumber = await _numberGenerator.GenerateEnquiryNumberAsync(cancellationToken);
        var enquiry = new Enquiry
        {
            EnquiryNumber = enquiryNumber,
            CustomerName = request.CustomerName.Trim(),
            Phone = request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Source = request.Source,
            Status = EnquiryStatus.New,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CustomerId = request.CustomerId
        };

        await BuildItemsAsync(enquiry, request.Items, cancellationToken);

        _context.Enquiries.Add(enquiry);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Create,
            "Enquiries",
            nameof(Enquiry),
            enquiry.Id.ToString(),
            enquiry.EnquiryNumber,
            after: new { enquiry.EnquiryNumber, enquiry.CustomerName, enquiry.Phone, Status = enquiry.Status.ToString(), ItemCount = enquiry.Items.Count },
            cancellationToken: cancellationToken);

        return MapToDto(enquiry);
    }

    public async Task<EnquiryDto> UpdateEnquiryAsync(Guid id, UpdateEnquiryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new DomainException("Enquiry must contain at least one item.");
        }

        var enquiry = await _context.Enquiries
            .Include(e => e.Items)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Enquiry), id);

        if (request.CustomerId.HasValue)
        {
            var customerExists = await _context.Customers
                .AnyAsync(c => c.Id == request.CustomerId.Value && !c.IsDeleted, cancellationToken);
            if (!customerExists)
            {
                throw new ResourceNotFoundException(nameof(Customer), request.CustomerId.Value);
            }
        }

        var before = new { enquiry.CustomerName, enquiry.Phone, ItemCount = enquiry.Items.Count };

        enquiry.CustomerName = request.CustomerName.Trim();
        enquiry.Phone = request.Phone.Trim();
        enquiry.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        enquiry.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        enquiry.Source = request.Source;
        enquiry.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        enquiry.CustomerId = request.CustomerId;

        // Replace items
        _context.EnquiryItems.RemoveRange(enquiry.Items);
        enquiry.Items.Clear();
        await BuildItemsAsync(enquiry, request.Items, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Enquiries",
            nameof(Enquiry),
            enquiry.Id.ToString(),
            enquiry.EnquiryNumber,
            before: before,
            after: new { enquiry.CustomerName, enquiry.Phone, ItemCount = enquiry.Items.Count },
            cancellationToken: cancellationToken);

        return MapToDto(enquiry);
    }

    public async Task<EnquiryDto> UpdateEnquiryStatusAsync(Guid id, UpdateEnquiryStatusRequest request, CancellationToken cancellationToken = default)
    {
        var enquiry = await _context.Enquiries
            .Include(e => e.Items)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Enquiry), id);

        if (!Enum.IsDefined(request.Status))
        {
            throw new DomainException("Invalid enquiry status.");
        }

        var oldStatus = enquiry.Status;
        enquiry.Status = request.Status;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            enquiry.Notes = string.IsNullOrWhiteSpace(enquiry.Notes)
                ? request.Note.Trim()
                : $"{enquiry.Notes}\n{request.Note.Trim()}";
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Update,
            "Enquiries",
            nameof(Enquiry),
            enquiry.Id.ToString(),
            enquiry.EnquiryNumber,
            before: new { Status = oldStatus.ToString() },
            after: new { Status = enquiry.Status.ToString() },
            cancellationToken: cancellationToken);

        return MapToDto(enquiry);
    }

    public async Task<bool> DeleteEnquiryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var enquiry = await _context.Enquiries
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken)
            ?? throw new ResourceNotFoundException(nameof(Enquiry), id);

        enquiry.IsDeleted = true;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.Delete,
            "Enquiries",
            nameof(Enquiry),
            enquiry.Id.ToString(),
            enquiry.EnquiryNumber,
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<List<EnquiryCustomerDto>> GetEnquiryCustomersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.Enquiries
            .AsNoTracking()
            .Where(e => !e.IsDeleted)
            .Select(e => new { e.CustomerName, e.Phone, e.Email, e.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Phone)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.CreatedAtUtc).First();
                return new EnquiryCustomerDto
                {
                    CustomerName = latest.CustomerName,
                    Phone = g.Key,
                    Email = latest.Email,
                    EnquiryCount = g.Count(),
                    LastEnquiryDateUtc = latest.CreatedAtUtc
                };
            })
            .OrderByDescending(c => c.LastEnquiryDateUtc)
            .ToList();
    }

    private async Task BuildItemsAsync(Enquiry enquiry, List<CreateEnquiryItemRequest> itemRequests, CancellationToken cancellationToken)
    {
        var productIds = itemRequests
            .Where(i => i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value)
            .Distinct()
            .ToList();

        var products = productIds.Count > 0
            ? await _context.Products
                .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
                .ToDictionaryAsync(p => p.Id, cancellationToken)
            : new Dictionary<Guid, Product>();

        foreach (var itemReq in itemRequests)
        {
            Product? product = null;
            if (itemReq.ProductId.HasValue && !products.TryGetValue(itemReq.ProductId.Value, out product))
            {
                throw new ResourceNotFoundException(nameof(Product), itemReq.ProductId.Value);
            }

            var productName = string.IsNullOrWhiteSpace(itemReq.ProductName)
                ? product?.Name
                : itemReq.ProductName.Trim();

            if (string.IsNullOrWhiteSpace(productName))
            {
                throw new DomainException("Each enquiry item requires a product name.");
            }

            if (itemReq.Quantity <= 0)
            {
                throw new DomainException($"Quantity for item '{productName}' must be greater than zero.");
            }

            enquiry.Items.Add(new EnquiryItem
            {
                ProductId = itemReq.ProductId,
                ProductName = productName,
                Quantity = itemReq.Quantity,
                ExpectedPrice = itemReq.ExpectedPrice,
                QuotedPrice = itemReq.QuotedPrice,
                Note = string.IsNullOrWhiteSpace(itemReq.Note) ? null : itemReq.Note.Trim()
            });
        }
    }

    private static EnquiryDto MapToDto(Enquiry e)
    {
        return new EnquiryDto
        {
            Id = e.Id,
            EnquiryNumber = e.EnquiryNumber,
            CustomerName = e.CustomerName,
            Phone = e.Phone,
            Email = e.Email,
            Address = e.Address,
            Source = e.Source.ToString(),
            Status = e.Status.ToString(),
            Notes = e.Notes,
            CustomerId = e.CustomerId,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc,
            Items = e.Items
                .OrderBy(i => i.CreatedAtUtc)
                .Select(i => new EnquiryItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    ExpectedPrice = i.ExpectedPrice,
                    QuotedPrice = i.QuotedPrice,
                    Note = i.Note
                }).ToList()
        };
    }
}
