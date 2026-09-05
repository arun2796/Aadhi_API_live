using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;
    private readonly ICurrentUserService _currentUser;

    public WishlistController(IWishlistService wishlistService, ICurrentUserService currentUser)
    {
        _wishlistService = wishlistService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.Cart)]
    public async Task<ActionResult<ApiResponse<List<WishlistItemDto>>>> GetWishlist(CancellationToken cancellationToken)
    {
        var items = await _wishlistService.GetMyWishlistAsync(cancellationToken);
        return Ok(ApiResponse<List<WishlistItemDto>>.Ok(items, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("{productId:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.Cart)]
    public async Task<ActionResult<ApiResponse<WishlistItemDto>>> AddToWishlist(Guid productId, CancellationToken cancellationToken)
    {
        var item = await _wishlistService.AddAsync(productId, cancellationToken);
        return Ok(ApiResponse<WishlistItemDto>.Ok(item, "Product added to wishlist", _currentUser.CorrelationId));
    }

    [HttpDelete("{productId:guid}")]
    [EnableRateLimiting(RateLimitingPolicies.Cart)]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveFromWishlist(Guid productId, CancellationToken cancellationToken)
    {
        var success = await _wishlistService.RemoveAsync(productId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Product removed from wishlist", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly IAddressService _addressService;
    private readonly ICurrentUserService _currentUser;

    public AddressesController(IAddressService addressService, ICurrentUserService currentUser)
    {
        _addressService = addressService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AddressDto>>>> GetAddresses(CancellationToken cancellationToken)
    {
        var addresses = await _addressService.GetMyAddressesAsync(cancellationToken);
        return Ok(ApiResponse<List<AddressDto>>.Ok(addresses, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AddressDto>>> CreateAddress([FromBody] AddressDto request, CancellationToken cancellationToken)
    {
        var created = await _addressService.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<AddressDto>.Ok(created, "Address added successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> UpdateAddress(Guid id, [FromBody] AddressDto request, CancellationToken cancellationToken)
    {
        var updated = await _addressService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<AddressDto>.Ok(updated, "Address updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAddress(Guid id, CancellationToken cancellationToken)
    {
        var success = await _addressService.DeleteAsync(id, cancellationToken);
        if (!success)
            return NotFound(ApiResponse<bool>.Fail($"Address with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<bool>.Ok(true, "Address deleted successfully", _currentUser.CorrelationId));
    }

    [HttpPost("{id:guid}/set-default")]
    public async Task<ActionResult<ApiResponse<bool>>> SetDefaultAddress(Guid id, CancellationToken cancellationToken)
    {
        var success = await _addressService.SetDefaultAsync(id, cancellationToken);
        if (!success)
            return NotFound(ApiResponse<bool>.Fail($"Address with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<bool>.Ok(true, "Default address updated successfully", _currentUser.CorrelationId));
    }
}
