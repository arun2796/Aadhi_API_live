using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class BannersController : ControllerBase
{
    private readonly IBannerService _bannerService;
    private readonly ICurrentUserService _currentUser;

    public BannersController(IBannerService bannerService, ICurrentUserService currentUser)
    {
        _bannerService = bannerService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<HomepageBannerDto>>>> GetBanners(
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? placement = null,
        CancellationToken cancellationToken = default)
    {
        var banners = await _bannerService.GetBannersAsync(activeOnly, placement, cancellationToken);
        return Ok(ApiResponse<List<HomepageBannerDto>>.Ok(banners, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<HomepageBannerDto>>> GetBannerById(Guid id, CancellationToken cancellationToken)
    {
        var banner = await _bannerService.GetBannerByIdAsync(id, cancellationToken);
        if (banner == null)
            return NotFound(ApiResponse<HomepageBannerDto>.Fail("Banner not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<HomepageBannerDto>.Ok(banner, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<HomepageBannerDto>>> CreateBanner([FromBody] CreateHomepageBannerRequest request, CancellationToken cancellationToken)
    {
        var banner = await _bannerService.CreateBannerAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetBannerById), new { id = banner.Id },
            ApiResponse<HomepageBannerDto>.Ok(banner, "Banner created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<HomepageBannerDto>>> UpdateBanner(Guid id, [FromBody] UpdateHomepageBannerRequest request, CancellationToken cancellationToken)
    {
        var banner = await _bannerService.UpdateBannerAsync(id, request, cancellationToken);
        return Ok(ApiResponse<HomepageBannerDto>.Ok(banner, "Banner updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBanner(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bannerService.DeleteBannerAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, "Banner deleted successfully", _currentUser.CorrelationId));
    }
}
