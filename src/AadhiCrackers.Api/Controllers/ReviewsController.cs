using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly ICurrentUserService _currentUser;

    public ReviewsController(IReviewService reviewService, ICurrentUserService currentUser)
    {
        _reviewService = reviewService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductReviewDto>>>> GetReviews(
        [FromQuery] Guid? productId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviewService.GetReviewsAsync(productId, status, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductReviewDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ProductReviewDto>>> GetReviewById(Guid id, CancellationToken cancellationToken)
    {
        var review = await _reviewService.GetReviewByIdAsync(id, cancellationToken);
        if (review == null)
            return NotFound(ApiResponse<ProductReviewDto>.Fail("Review not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<ProductReviewDto>.Ok(review, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [AllowAnonymous] // Supports both guest and verified customer reviews
    public async Task<ActionResult<ApiResponse<ProductReviewDto>>> SubmitReview([FromBody] CreateProductReviewRequest request, CancellationToken cancellationToken)
    {
        var review = await _reviewService.CreateReviewAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetReviewById), new { id = review.Id },
            ApiResponse<ProductReviewDto>.Ok(review, "Review submitted successfully and is pending moderation.", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<ProductReviewDto>>> UpdateReviewStatus(
        Guid id,
        [FromBody] UpdateReviewStatusRequest request,
        CancellationToken cancellationToken)
    {
        var review = await _reviewService.UpdateReviewStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ProductReviewDto>.Ok(review, $"Review marked as {review.Status}", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteReview(Guid id, CancellationToken cancellationToken)
    {
        var result = await _reviewService.DeleteReviewAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, "Review deleted successfully", _currentUser.CorrelationId));
    }
}
