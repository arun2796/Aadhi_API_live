using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Application.Services;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly ICurrentUserService _currentUser;

    public ProductsController(ICatalogService catalogService, ICurrentUserService currentUser)
    {
        _catalogService = catalogService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProducts([FromQuery] ProductFilterRequest filter, CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetProductsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(result, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{slug}")]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> GetProductBySlug(string slug, CancellationToken cancellationToken)
    {
        var product = await _catalogService.GetProductBySlugAsync(slug, cancellationToken);
        if (product == null)
            return NotFound(ApiResponse<ProductDetailDto>.Fail($"Product with slug '{slug}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<ProductDetailDto>.Ok(product, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("id/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _catalogService.GetProductByIdAsync(id, cancellationToken);
        if (product == null)
            return NotFound(ApiResponse<ProductDetailDto>.Fail($"Product with id '{id}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<ProductDetailDto>.Ok(product, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("featured")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetFeaturedProducts([FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetFeaturedProductsAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("best-sellers")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetBestSellers([FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetBestSellersAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("new-arrivals")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetNewArrivals([FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetNewArrivalsAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("gift-boxes")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetGiftBoxes([FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetGiftBoxesAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("combo-offers")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetComboOffers([FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetComboOffersAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("low-stock")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetLowStock([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _catalogService.GetLowStockProductsAsync(count, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetProductById), new { id = result.Id }, ApiResponse<ProductDetailDto>.Ok(result, "Product created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        request.Id = id;
        var result = await _catalogService.UpdateProductAsync(request, cancellationToken);
        return Ok(ApiResponse<ProductDetailDto>.Ok(result, "Product updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var success = await _catalogService.DeleteProductAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Product deleted successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly ICurrentUserService _currentUser;

    public CategoriesController(ICatalogService catalogService, ICurrentUserService currentUser)
    {
        _catalogService = catalogService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<List<CategoryDto>>>> GetCategories([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var categories = await _catalogService.GetCategoriesAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<List<CategoryDto>>.Ok(categories, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("{slug}")]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> GetCategoryBySlug(string slug, CancellationToken cancellationToken)
    {
        var category = await _catalogService.GetCategoryBySlugAsync(slug, cancellationToken);
        if (category == null)
            return NotFound(ApiResponse<CategoryDto>.Fail($"Category with slug '{slug}' not found", _currentUser.CorrelationId));

        return Ok(ApiResponse<CategoryDto>.Ok(category, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.CreateCategoryAsync(request, cancellationToken);
        return Ok(ApiResponse<CategoryDto>.Ok(result, "Category created successfully", _currentUser.CorrelationId));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        request.Id = id;
        var result = await _catalogService.UpdateCategoryAsync(request, cancellationToken);
        return Ok(ApiResponse<CategoryDto>.Ok(result, "Category updated successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        var success = await _catalogService.DeleteCategoryAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Category deleted successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class BrandsController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly ICurrentUserService _currentUser;

    public BrandsController(ICatalogService catalogService, ICurrentUserService currentUser)
    {
        _catalogService = catalogService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BrandDto>>>> GetBrands([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var brands = await _catalogService.GetBrandsAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<List<BrandDto>>.Ok(brands, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<BrandDto>>> CreateBrand([FromBody] CreateBrandRequest request, CancellationToken cancellationToken)
    {
        var result = await _catalogService.CreateBrandAsync(request, cancellationToken);
        return Ok(ApiResponse<BrandDto>.Ok(result, "Brand created successfully", _currentUser.CorrelationId));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBrand(Guid id, CancellationToken cancellationToken)
    {
        var result = await _catalogService.DeleteBrandAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(result, "Brand deleted successfully", _currentUser.CorrelationId));
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly ICurrentUserService _currentUser;

    public CartController(ICartService cartService, ICurrentUserService currentUser)
    {
        _cartService = cartService;
        _currentUser = currentUser;
    }

    public class CalculateCartPayload
    {
        public List<AddToCartRequest> Items { get; set; } = new();
        public string? CouponCode { get; set; }
    }

    [HttpPost("calculate")]
    [EnableRateLimiting(RateLimitingPolicies.Cart)]
    public async Task<ActionResult<ApiResponse<CartDto>>> CalculateCart([FromBody] CalculateCartPayload payload, CancellationToken cancellationToken)
    {
        var cart = await _cartService.CalculateCartAsync(payload.Items, payload.CouponCode, cancellationToken);
        return Ok(ApiResponse<CartDto>.Ok(cart, correlationId: _currentUser.CorrelationId));
    }
}
