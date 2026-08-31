using System.Security.Claims;
using AadhiCrackers.Application.Common.Interfaces;

namespace AadhiCrackers.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ??
        _httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");

    public string? UserName =>
        _httpContextAccessor.HttpContext?.User?.Identity?.Name ??
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public string? Email =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public string? Role =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role) ??
        (_httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true ? "Customer" : "Anonymous");

    public string? IpAddress =>
        _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ??
        _httpContextAccessor.HttpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
        "127.0.0.1";

    public string? UserAgent =>
        _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString() ??
        _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault() ??
        Guid.NewGuid().ToString("N");

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
