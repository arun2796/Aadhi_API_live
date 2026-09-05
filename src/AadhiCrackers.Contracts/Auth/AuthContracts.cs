namespace AadhiCrackers.Contracts.Auth;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty; // email OR phone number (fallback when Email is empty)
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class FirebaseLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhoneNumber { get; set; }
}

public class RegisterRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class CreateStaffUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Identifier { get; set; } = string.Empty; // mobile or email
    public string Email { get; set; } = string.Empty; // legacy fallback
}

public class ForgotPasswordResponse
{
    public string Message { get; set; } = string.Empty;
    public string? DevOtp { get; set; } // populated ONLY in Development environment
}

public class VerifyOtpRequest
{
    public string Identifier { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}

public class VerifyOtpResponse
{
    public string ResetToken { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;

    // Legacy fields kept for back-compat
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsActive { get; set; }
    public int? RewardPoints { get; set; } // populated when the user is a customer
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public UserDto? User { get; set; }
    public string? Token { get; set; } // Optional bearer token for mobile/API clients
}

public class LoginHistoryDto
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
}

public class RateLimitLogDto
{
    public Guid Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int RequestsCount { get; set; }
    public int BlockedCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

