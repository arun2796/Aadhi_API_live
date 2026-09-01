using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Customers;
using AadhiCrackers.Contracts.Orders;
using AadhiCrackers.Contracts.Promotions;
using AadhiCrackers.Contracts.System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUser;

    public CustomersController(ICustomerService customerService, IOrderService orderService, ICurrentUserService currentUser)
    {
        _customerService = customerService;
        _orderService = orderService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireStaff")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<CustomerDto>>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _customerService.GetCustomersAsync(page, pageSize, search, cancellationToken);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomerById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetCustomerByIdAsync(id, cancellationToken);
        if (customer == null)
            return NotFound(ApiResponse<CustomerDto>.Fail($"Customer with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<CustomerDto>.Ok(customer, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}/orders")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetCustomerOrders(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.Role == "Customer" && Guid.TryParse(_currentUser.UserId, out var custId) && custId != id)
        {
            return Forbid();
        }

        var orders = await _orderService.GetCustomerOrdersAsync(id, cancellationToken);
        return Ok(ApiResponse<List<OrderDto>>.Ok(orders, correlationId: _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var updated = await _customerService.UpdateCustomerAsync(id, request, cancellationToken);
        return Ok(ApiResponse<CustomerDto>.Ok(updated, "Customer updated successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class PromotionsController : ControllerBase
{
    private readonly IPromotionService _promotionService;
    private readonly ICurrentUserService _currentUser;

    public PromotionsController(IPromotionService promotionService, ICurrentUserService currentUser)
    {
        _promotionService = promotionService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireStaff")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PagedResult<PromotionDto>>>> GetPromotions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _promotionService.GetPromotionsAsync(page, pageSize, search, activeOnly, cancellationToken);
        return Ok(ApiResponse<PagedResult<PromotionDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<ApiResponse<PromotionDto>>> GetPromotionById(Guid id, CancellationToken cancellationToken)
    {
        var promo = await _promotionService.GetPromotionByIdAsync(id, cancellationToken);
        if (promo == null)
            return NotFound(ApiResponse<PromotionDto>.Fail($"Promotion with ID '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<PromotionDto>.Ok(promo, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PromotionDto>>> CreatePromotion([FromBody] CreatePromotionRequest request, CancellationToken cancellationToken)
    {
        var promo = await _promotionService.CreatePromotionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPromotionById), new { id = promo.Id }, ApiResponse<PromotionDto>.Ok(promo, "Promotion created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<PromotionDto>>> UpdatePromotion(Guid id, [FromBody] UpdatePromotionRequest request, CancellationToken cancellationToken)
    {
        var promo = await _promotionService.UpdatePromotionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PromotionDto>.Ok(promo, "Promotion updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> DeletePromotion(Guid id, CancellationToken cancellationToken)
    {
        var success = await _promotionService.DeletePromotionAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Promotion deactivated/deleted successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/system-health")]
[Route("api/v1/system/health")]
public class SystemHealthController : ControllerBase
{
    private readonly ISystemHealthService _healthService;
    private readonly ICurrentUserService _currentUser;

    public SystemHealthController(ISystemHealthService healthService, ICurrentUserService currentUser)
    {
        _healthService = healthService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<SystemHealthDto>>> GetHealth(CancellationToken cancellationToken)
    {
        var health = await _healthService.GetSystemHealthAsync(cancellationToken);
        return Ok(ApiResponse<SystemHealthDto>.Ok(health, correlationId: _currentUser.CorrelationId));
    }
}
