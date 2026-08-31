using AadhiCrackers.Api.Middleware;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AadhiCrackers.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IIdentityService identityService, ICurrentUserService currentUser)
    {
        _identityService = identityService;
        _currentUser = currentUser;
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingPolicies.Login)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _identityService.AuthenticateAsync(request, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(response.Message ?? "Invalid credentials", _currentUser.CorrelationId));
        }

        // Set secure auth cookie where supported
        Response.Cookies.Append("AadhiAuth", response.Token ?? "session", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
        });

        return Ok(ApiResponse<AuthResponse>.Ok(response, "Login successful", _currentUser.CorrelationId));
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingPolicies.PublicGeneral)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await _identityService.RegisterAsync(request, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(response.Message ?? "Registration failed", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<AuthResponse>.Ok(response, "Registration successful", _currentUser.CorrelationId));
    }

    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Unauthorized(ApiResponse<UserDto>.Fail("Not authenticated", _currentUser.CorrelationId));
        }

        var user = await _identityService.GetUserByIdAsync(_currentUser.UserId, cancellationToken);
        if (user == null)
        {
            return NotFound(ApiResponse<UserDto>.Fail("User not found", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<UserDto>.Ok(user, correlationId: _currentUser.CorrelationId));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("AadhiAuth");
        return Ok(ApiResponse<bool>.Ok(true, "Logged out successfully", _currentUser.CorrelationId));
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            return Unauthorized(ApiResponse<bool>.Fail("Not authenticated", _currentUser.CorrelationId));

        var success = await _identityService.ChangePasswordAsync(_currentUser.UserId, request, cancellationToken);
        if (!success)
            return BadRequest(ApiResponse<bool>.Fail("Failed to change password. Ensure current password is correct.", _currentUser.CorrelationId));

        return Ok(ApiResponse<bool>.Ok(true, "Password changed successfully", _currentUser.CorrelationId));
    }

    [HttpGet("users")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetAllUsers(CancellationToken cancellationToken)
    {
        var users = await _identityService.GetAllUsersAsync(cancellationToken);
        return Ok(ApiResponse<List<UserDto>>.Ok(users, correlationId: _currentUser.CorrelationId));
    }

    [HttpPut("users/{id}/role")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserRole(string id, [FromBody] string role, CancellationToken cancellationToken)
    {
        var success = await _identityService.UpdateUserRoleAsync(id, role, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Role updated successfully", _currentUser.CorrelationId));
    }

    [HttpPut("users/{id}/status")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleUserStatus(string id, [FromBody] bool isActive, CancellationToken cancellationToken)
    {
        var success = await _identityService.ToggleUserStatusAsync(id, isActive, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Status updated successfully", _currentUser.CorrelationId));
    }
}
