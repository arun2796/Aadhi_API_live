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

    // Deliberately no IWebHostEnvironment: nothing in this controller may behave differently
    // (least of all more permissively) because the box happens to be running as Development.
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

    [HttpPost("firebase-login")]
    [EnableRateLimiting(RateLimitingPolicies.Login)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> FirebaseLogin([FromBody] FirebaseLoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _identityService.AuthenticateWithFirebaseAsync(request, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(response.Message ?? "Firebase authentication failed", _currentUser.CorrelationId));
        }

        // Set secure auth cookie
        Response.Cookies.Append("AadhiAuth", response.Token ?? "session", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return Ok(ApiResponse<AuthResponse>.Ok(response, "Firebase login successful", _currentUser.CorrelationId));
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

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingPolicies.PasswordReset)]
    public async Task<ActionResult<ApiResponse<ForgotPasswordResponse>>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        return await SendPasswordResetOtpAsync(request, cancellationToken);
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting(RateLimitingPolicies.PasswordReset)]
    public async Task<ActionResult<ApiResponse<ForgotPasswordResponse>>> ResendOtp([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        return await SendPasswordResetOtpAsync(request, cancellationToken);
    }

    private async Task<ActionResult<ApiResponse<ForgotPasswordResponse>>> SendPasswordResetOtpAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var identifier = !string.IsNullOrWhiteSpace(request.Identifier) ? request.Identifier : request.Email;

        // The OTP is a credential. It is delivered out of band (SMS/email, and in DEBUG builds the
        // application log) and is NEVER echoed in the HTTP response — not in Development either.
        // The previous IsDevelopment()-gated "devOtp" field made a full account takeover possible
        // with nothing but a phone number, one environment variable away from being live.
        _ = await _identityService.GeneratePasswordResetOtpAsync(identifier, cancellationToken);

        // Always 200 — never reveal whether an account exists.
        var response = new ForgotPasswordResponse
        {
            Message = "If an account exists for this mobile number or email, an OTP has been sent."
        };

        return Ok(ApiResponse<ForgotPasswordResponse>.Ok(response, response.Message, _currentUser.CorrelationId));
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting(RateLimitingPolicies.Login)]
    public async Task<ActionResult<ApiResponse<VerifyOtpResponse>>> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var resetToken = await _identityService.VerifyPasswordResetOtpAsync(request.Identifier, request.Otp, cancellationToken);
        if (string.IsNullOrWhiteSpace(resetToken))
        {
            return BadRequest(ApiResponse<VerifyOtpResponse>.Fail("Invalid or expired OTP.", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<VerifyOtpResponse>.Ok(new VerifyOtpResponse { ResetToken = resetToken }, "OTP verified successfully", _currentUser.CorrelationId));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingPolicies.Login)]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse<bool>.Fail("Passwords do not match.", _currentUser.CorrelationId));
        }

        var success = await _identityService.ResetPasswordWithTokenAsync(request.ResetToken, request.NewPassword, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail("Invalid or expired reset token, or the password does not meet the policy.", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Password reset successfully", _currentUser.CorrelationId));
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

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateCurrentUser([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Unauthorized(ApiResponse<UserDto>.Fail("Not authenticated", _currentUser.CorrelationId));
        }

        var response = await _identityService.UpdateProfileAsync(_currentUser.UserId, request, cancellationToken);
        if (!response.Success || response.User == null)
        {
            return BadRequest(ApiResponse<UserDto>.Fail(response.Message ?? "Failed to update profile", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<UserDto>.Ok(response.User, "Profile updated successfully", _currentUser.CorrelationId));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("AadhiAuth");
        return Ok(ApiResponse<bool>.Ok(true, "Logged out successfully", _currentUser.CorrelationId));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            return Unauthorized(ApiResponse<bool>.Fail("Not authenticated", _currentUser.CorrelationId));

        var success = await _identityService.ChangePasswordAsync(_currentUser.UserId, request, cancellationToken);
        if (!success)
            return BadRequest(ApiResponse<bool>.Fail("Failed to change password. Ensure current password is correct.", _currentUser.CorrelationId));

        return Ok(ApiResponse<bool>.Ok(true, "Password changed successfully", _currentUser.CorrelationId));
    }

    [HttpPost("users")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateStaffUser([FromBody] CreateStaffUserRequest request, CancellationToken cancellationToken)
    {
        var response = await _identityService.CreateStaffUserAsync(request, cancellationToken);
        if (!response.Success || response.User == null)
        {
            return BadRequest(ApiResponse<UserDto>.Fail(response.Message ?? "Failed to create user", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<UserDto>.Ok(response.User, "User created successfully", _currentUser.CorrelationId));
    }

    [HttpGet("users")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetAllUsers(CancellationToken cancellationToken)
    {
        var users = await _identityService.GetAllUsersAsync(cancellationToken);
        return Ok(ApiResponse<List<UserDto>>.Ok(users, correlationId: _currentUser.CorrelationId));
    }

    [HttpPut("users/{id}/role")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserRole(string id, [FromBody] System.Text.Json.JsonElement payload, CancellationToken cancellationToken)
    {
        string role = payload.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => payload.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Object when payload.TryGetProperty("role", out var prop) => prop.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Object when payload.TryGetProperty("newRole", out var prop2) => prop2.GetString() ?? string.Empty,
            _ => payload.ToString()
        };

        var success = await _identityService.UpdateUserRoleAsync(id, role, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Failed to update user role to '{role}'. Ensure the role is valid.", _currentUser.CorrelationId));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Role updated successfully", _currentUser.CorrelationId));
    }

    [HttpPut("users/{id}/status")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleUserStatus(string id, [FromBody] System.Text.Json.JsonElement payload, CancellationToken cancellationToken)
    {
        bool isActive = payload.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Object when payload.TryGetProperty("isActive", out var prop) => prop.GetBoolean(),
            _ => true
        };

        var success = await _identityService.ToggleUserStatusAsync(id, isActive, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Status updated successfully", _currentUser.CorrelationId));
    }

    [HttpGet("login-history")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<List<LoginHistoryDto>>>> GetLoginHistory(CancellationToken cancellationToken)
    {
        var history = await _identityService.GetLoginHistoryAsync(cancellationToken);
        return Ok(ApiResponse<List<LoginHistoryDto>>.Ok(history, correlationId: _currentUser.CorrelationId));
    }

    [HttpGet("rate-limit-logs")]
    [Authorize(Policy = "RequireAdmin")]
    [EnableRateLimiting(RateLimitingPolicies.AdminApi)]
    public async Task<ActionResult<ApiResponse<List<RateLimitLogDto>>>> GetRateLimitLogs(CancellationToken cancellationToken)
    {
        var logs = await _identityService.GetRateLimitLogsAsync(cancellationToken);
        return Ok(ApiResponse<List<RateLimitLogDto>>.Ok(logs, correlationId: _currentUser.CorrelationId));
    }
}
