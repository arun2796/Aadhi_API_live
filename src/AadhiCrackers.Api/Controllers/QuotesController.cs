using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Contracts.Quotes;
using AadhiCrackers.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class QuotesController : ControllerBase
{
    private readonly IQuoteService _quoteService;
    private readonly ICurrentUserService _currentUser;

    public QuotesController(IQuoteService quoteService, ICurrentUserService currentUser)
    {
        _quoteService = quoteService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireSalesExecutive")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<QuoteDto>>>> GetQuotes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] QuoteStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _quoteService.GetQuotesAsync(page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<QuoteDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireSalesExecutive")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> GetQuoteById(Guid id, CancellationToken cancellationToken)
    {
        var quote = await _quoteService.GetQuoteByIdAsync(id, cancellationToken);
        if (quote == null)
        {
            return NotFound(ApiResponse<QuoteDto>.Fail("Quote not found", correlationId: _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<QuoteDto>.Ok(quote, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireSalesExecutive")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> CreateQuote([FromBody] CreateQuoteRequest request, CancellationToken cancellationToken)
    {
        var quote = await _quoteService.CreateQuoteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetQuoteById), new { id = quote.Id }, ApiResponse<QuoteDto>.Ok(quote, "Quote created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "RequireSalesExecutive")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<QuoteDto>>> UpdateQuoteStatus(Guid id, [FromBody] UpdateQuoteStatusRequest request, CancellationToken cancellationToken)
    {
        var quote = await _quoteService.UpdateQuoteStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<QuoteDto>.Ok(quote, "Quote status updated successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = "RequireSalesExecutive")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ConvertQuoteToOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await _quoteService.ConvertQuoteToOrderAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDto>.Ok(order, "Quote successfully converted to live Order", _currentUser.CorrelationId));
    }
}
