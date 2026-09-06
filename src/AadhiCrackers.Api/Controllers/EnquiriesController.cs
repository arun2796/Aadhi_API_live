using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Enquiries;
using AadhiCrackers.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = "RequireStaff")]
public class EnquiriesController : ControllerBase
{
    private readonly IEnquiryService _enquiryService;
    private readonly ICurrentUserService _currentUser;

    public EnquiriesController(IEnquiryService enquiryService, ICurrentUserService currentUser)
    {
        _enquiryService = enquiryService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<EnquiryDto>>>> GetEnquiries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] EnquiryStatus? status = null,
        [FromQuery] EnquirySource? source = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _enquiryService.GetEnquiriesAsync(page, pageSize, status, source, search, cancellationToken);
        return Ok(ApiResponse<PagedResult<EnquiryDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("customers")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<List<EnquiryCustomerDto>>>> GetEnquiryCustomers(CancellationToken cancellationToken)
    {
        var customers = await _enquiryService.GetEnquiryCustomersAsync(cancellationToken);
        return Ok(ApiResponse<List<EnquiryCustomerDto>>.Ok(customers, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<EnquiryDto>>> GetEnquiryById(Guid id, CancellationToken cancellationToken)
    {
        var enquiry = await _enquiryService.GetEnquiryByIdAsync(id, cancellationToken);
        if (enquiry == null)
        {
            return NotFound(ApiResponse<EnquiryDto>.Fail("Enquiry not found", correlationId: _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<EnquiryDto>.Ok(enquiry, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<EnquiryDto>>> CreateEnquiry([FromBody] CreateEnquiryRequest request, CancellationToken cancellationToken)
    {
        var enquiry = await _enquiryService.CreateEnquiryAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetEnquiryById), new { id = enquiry.Id },
            ApiResponse<EnquiryDto>.Ok(enquiry, "Enquiry created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<EnquiryDto>>> UpdateEnquiry(Guid id, [FromBody] UpdateEnquiryRequest request, CancellationToken cancellationToken)
    {
        var enquiry = await _enquiryService.UpdateEnquiryAsync(id, request, cancellationToken);
        return Ok(ApiResponse<EnquiryDto>.Ok(enquiry, "Enquiry updated successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}/status")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<EnquiryDto>>> UpdateEnquiryStatus(Guid id, [FromBody] UpdateEnquiryStatusRequest request, CancellationToken cancellationToken)
    {
        var enquiry = await _enquiryService.UpdateEnquiryStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<EnquiryDto>.Ok(enquiry, "Enquiry status updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteEnquiry(Guid id, CancellationToken cancellationToken)
    {
        var result = await _enquiryService.DeleteEnquiryAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, "Enquiry deleted successfully", _currentUser.CorrelationId));
    }
}
