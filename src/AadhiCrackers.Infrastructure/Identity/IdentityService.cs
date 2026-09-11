using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AadhiCrackers.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService? _currentUser;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext context,
        IConfiguration configuration,
        INotificationService notificationService,
        ICurrentUserService? currentUser = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
        _configuration = configuration;
        _notificationService = notificationService;
        _currentUser = currentUser;
    }

    public async Task<AuthResponse> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // Back-compat: prefer the explicit email field; otherwise resolve the identifier
        // (email when it contains '@', else lookup by phone number).
        var loginKey = !string.IsNullOrWhiteSpace(request.Email) ? request.Email.Trim() : request.Identifier.Trim();
        var user = await ResolveUserByIdentifierAsync(loginKey, cancellationToken);
        if (user == null)
        {
            return new AuthResponse { Success = false, Message = "Invalid email or password." };
        }

        if (!user.IsActive)
        {
            return new AuthResponse { Success = false, Message = "Your account is deactivated. Please contact support." };
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        
        // Record Login History
        try
        {
            _context.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id,
                Email = user.Email ?? loginKey,
                IpAddress = _currentUser?.IpAddress,
                UserAgent = _currentUser?.UserAgent,
                TimestampUtc = DateTime.UtcNow,
                Success = result.Succeeded,
                FailureReason = result.Succeeded ? null : (result.IsLockedOut ? "Account is locked out" : "Invalid password")
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch { /* non-blocking audit */ }

        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
                return new AuthResponse { Success = false, Message = "Account is temporarily locked out due to multiple failed login attempts." };

            return new AuthResponse { Success = false, Message = "Invalid email or password." };
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? AppRoles.Customer;
        var permissions = GetPermissionsForRole(role);

        var token = GenerateJwtToken(user, role, permissions);

        var userDto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber ?? string.Empty,
            Role = role,
            Permissions = permissions,
            IsActive = user.IsActive,
            RewardPoints = await GetRewardPointsAsync(user.Id, role, cancellationToken)
        };

        return new AuthResponse
        {
            Success = true,
            Message = "Login successful",
            User = userDto,
            Token = token
        };
    }

    // Firebase ID-token verification: Google's OIDC metadata (issuer + JWKS signing keys) is
    // fetched once per project and cached process-wide; ConfigurationManager refreshes it
    // automatically (and on demand via RequestRefresh when a key rotates).
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _firebaseConfigManagers = new();

    private static ConfigurationManager<OpenIdConnectConfiguration> GetFirebaseConfigurationManager(string projectId)
    {
        return _firebaseConfigManagers.GetOrAdd(projectId, pid =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                $"https://securetoken.google.com/{pid}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true }));
    }

    public async Task<AuthResponse> AuthenticateWithFirebaseAsync(FirebaseLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return new AuthResponse { Success = false, Message = "Firebase token is required." };
        }

        var projectId = _configuration["Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return new AuthResponse { Success = false, Message = "Firebase sign-in is not configured on the server." };
        }

        var configManager = GetFirebaseConfigurationManager(projectId);

        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AuthResponse { Success = false, Message = "Could not reach Firebase verification service. Please try again later." };
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{projectId}",
            ValidateAudience = true,
            ValidAudience = projectId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        ClaimsPrincipal principal;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            try
            {
                principal = handler.ValidateToken(request.IdToken, validationParameters, out _);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                // Google rotates its signing keys; force a metadata refresh and retry once.
                configManager.RequestRefresh();
                oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);
                validationParameters.IssuerSigningKeys = oidcConfig.SigningKeys;
                principal = handler.ValidateToken(request.IdToken, validationParameters, out _);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Bad signature, wrong issuer/audience, expired, malformed, unreachable metadata on
            // the retry — the token is not trusted. NEVER fall back to request-provided identity.
            return new AuthResponse { Success = false, Message = "Invalid or expired Firebase credentials." };
        }

        // Identity comes ONLY from the cryptographically verified token's claims.
        string email = principal.FindFirst("email")?.Value?.Trim() ?? string.Empty;
        string phone = principal.FindFirst("phone_number")?.Value?.Trim() ?? string.Empty;
        string firebaseUid = principal.FindFirst("sub")?.Value ?? string.Empty;
        string name = principal.FindFirst("name")?.Value?.Trim() ?? string.Empty;

        // DisplayName from the request is cosmetic only (used when the token carries no name);
        // it never participates in identity resolution.
        if (string.IsNullOrWhiteSpace(name))
        {
            name = request.DisplayName?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
        {
            return new AuthResponse { Success = false, Message = "Firebase credentials did not include an email or phone number." };
        }

        var lookupKey = !string.IsNullOrWhiteSpace(email) ? email : phone;
        var user = await ResolveUserByIdentifierAsync(lookupKey, cancellationToken);

        if (user != null)
        {
            // Social login must never open a path into staff/admin accounts.
            var existingRoles = await _userManager.GetRolesAsync(user);
            if (existingRoles.Count > 0 && !existingRoles.Contains(AppRoles.Customer))
            {
                return new AuthResponse { Success = false, Message = "Please sign in with your email and password." };
            }
        }

        if (user == null)
        {
            // Auto-provision Customer
            var count = await _context.Customers.CountAsync(cancellationToken) + 1;
            var customerCode = $"CUST-{DateTime.UtcNow:yyMM}-{count:D4}";

            string firstName = "Customer";
            string lastName = string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) firstName = parts[0];
                if (parts.Length > 1) lastName = string.Join(" ", parts.Skip(1));
            }
            else if (!string.IsNullOrWhiteSpace(email))
            {
                firstName = email.Split('@')[0];
            }

            user = new ApplicationUser
            {
                UserName = !string.IsNullOrWhiteSpace(email) ? email : phone,
                Email = !string.IsNullOrWhiteSpace(email) ? email : $"{customerCode.ToLower()}@customer.aadhicracker.in",
                PhoneNumber = phone,
                FirstName = firstName,
                LastName = lastName,
                CustomerCode = customerCode,
                EmailConfirmed = true,
                PhoneNumberConfirmed = !string.IsNullOrWhiteSpace(phone),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var createRes = await _userManager.CreateAsync(user);
            if (!createRes.Succeeded)
            {
                var errors = string.Join(", ", createRes.Errors.Select(e => e.Description));
                return new AuthResponse { Success = false, Message = $"Failed to create user account: {errors}" };
            }

            await _userManager.AddToRoleAsync(user, AppRoles.Customer);

            var customer = new Customer
            {
                UserId = user.Id,
                CustomerCode = customerCode,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Phone = user.PhoneNumber,
                IsActive = true
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync(cancellationToken);
        }

        if (!user.IsActive)
        {
            return new AuthResponse { Success = false, Message = "Your account is deactivated. Please contact support." };
        }

        // Record Login History
        try
        {
            _context.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id,
                Email = user.Email ?? lookupKey,
                IpAddress = _currentUser?.IpAddress,
                UserAgent = _currentUser?.UserAgent,
                TimestampUtc = DateTime.UtcNow,
                Success = true,
                FailureReason = null
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch { /* non-blocking audit */ }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? AppRoles.Customer;
        var permissions = GetPermissionsForRole(role);

        var token = GenerateJwtToken(user, role, permissions);

        var userDto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber ?? string.Empty,
            Role = role,
            Permissions = permissions,
            IsActive = user.IsActive,
            RewardPoints = await GetRewardPointsAsync(user.Id, role, cancellationToken)
        };

        return new AuthResponse
        {
            Success = true,
            Message = "Firebase login successful",
            User = userDto,
            Token = token
        };
    }

    private async Task<ApplicationUser?> ResolveUserByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        var key = identifier.Trim();
        if (key.Contains('@'))
        {
            return await _userManager.FindByEmailAsync(key);
        }

        return await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == key, cancellationToken)
            ?? await _userManager.FindByEmailAsync(key);
    }

    private async Task<int?> GetRewardPointsAsync(string userId, string role, CancellationToken cancellationToken)
    {
        if (!string.Equals(role, AppRoles.Customer, StringComparison.OrdinalIgnoreCase)) return null;

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsDeleted, cancellationToken);

        return customer?.RewardPoints ?? 0;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing != null)
        {
            return new AuthResponse { Success = false, Message = "An account with this email address already exists." };
        }

        var count = await _context.Customers.CountAsync(cancellationToken) + 1;
        var customerCode = $"CUST-{DateTime.UtcNow:yyMM}-{count:D4}";

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            PhoneNumber = request.Phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            CustomerCode = customerCode,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return new AuthResponse { Success = false, Message = errors };
        }

        // Assign Customer role by default
        await _userManager.AddToRoleAsync(user, AppRoles.Customer);

        // Create linked Customer entity in business database
        var existingCustomer = await _context.Customers.FirstOrDefaultAsync(c => c.Email == user.Email, cancellationToken);
        if (existingCustomer != null)
        {
            existingCustomer.UserId = user.Id;
            existingCustomer.FirstName = user.FirstName;
            existingCustomer.LastName = user.LastName;
            existingCustomer.Phone = user.PhoneNumber;
        }
        else
        {
            var customer = new Customer
            {
                UserId = user.Id,
                CustomerCode = customerCode,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Phone = user.PhoneNumber,
                IsActive = true
            };
            _context.Customers.Add(customer);
        }
        await _context.SaveChangesAsync(cancellationToken);

        var permissions = GetPermissionsForRole(AppRoles.Customer);
        var token = GenerateJwtToken(user, AppRoles.Customer, permissions);

        var userDto = new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber,
            Role = AppRoles.Customer,
            Permissions = permissions,
            IsActive = true
        };

        return new AuthResponse
        {
            Success = true,
            Message = "Registration successful",
            User = userDto,
            Token = token
        };
    }

    public async Task<AuthResponse> CreateStaffUserAsync(CreateStaffUserRequest request, CancellationToken cancellationToken = default)
    {
        var staffRoles = new[]
        {
            AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Manager, AppRoles.SalesExecutive,
            AppRoles.InventoryManager, AppRoles.PurchaseManager, AppRoles.Accountant, AppRoles.SupportAgent
        };

        var requestedRole = request.Role?.Trim() ?? string.Empty;
        var role = staffRoles.FirstOrDefault(r => string.Equals(r, requestedRole, StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            return new AuthResponse
            {
                Success = false,
                Message = $"Invalid role '{requestedRole}'. Valid roles: {string.Join(", ", staffRoles)}."
            };
        }

        var email = request.Email.Trim();
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return new AuthResponse { Success = false, Message = "An account with this email address already exists." };
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName?.Trim() ?? string.Empty,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return new AuthResponse { Success = false, Message = errors };
        }

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            // Roll back the half-created account so a retry can succeed.
            await _userManager.DeleteAsync(user);
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            return new AuthResponse { Success = false, Message = $"Failed to assign role '{role}': {errors}" };
        }

        // Deliberately no token and no sign-in: the admin creates the account,
        // the new staff member logs in themselves.
        return new AuthResponse
        {
            Success = true,
            Message = "User created successfully",
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.PhoneNumber ?? string.Empty,
                Role = role,
                Permissions = GetPermissionsForRole(role),
                IsActive = user.IsActive
            }
        };
    }

    public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded;
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? AppRoles.Customer;

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber ?? string.Empty,
            Role = role,
            Permissions = GetPermissionsForRole(role),
            IsActive = user.IsActive,
            RewardPoints = await GetRewardPointsAsync(user.Id, role, cancellationToken)
        };
    }

    public async Task<AuthResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new AuthResponse { Success = false, Message = "User not found." };
        }

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var phone = request.Phone?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName))
        {
            return new AuthResponse { Success = false, Message = "First name is required." };
        }

        if (!string.IsNullOrEmpty(phone) && (phone.Length != 10 || !phone.All(char.IsDigit)))
        {
            return new AuthResponse { Success = false, Message = "Phone number must be exactly 10 digits." };
        }

        if (!string.IsNullOrEmpty(phone))
        {
            var phoneTakenByUser = await _userManager.Users
                .AnyAsync(u => u.PhoneNumber == phone && u.Id != user.Id, cancellationToken);
            var phoneTakenByCustomer = await _context.Customers
                .AnyAsync(c => c.Phone == phone && c.UserId != user.Id && !c.IsDeleted, cancellationToken);

            if (phoneTakenByUser || phoneTakenByCustomer)
            {
                return new AuthResponse { Success = false, Message = "This phone number is already in use by another account." };
            }
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = string.IsNullOrEmpty(phone) ? null : phone;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return new AuthResponse { Success = false, Message = $"Failed to update profile: {errors}" };
        }

        // Keep the linked Customer entity in sync (linked by UserId; email is the legacy fallback).
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.UserId == user.Id && !c.IsDeleted, cancellationToken)
            ?? await _context.Customers.FirstOrDefaultAsync(c => c.Email == user.Email && !c.IsDeleted, cancellationToken);

        if (customer != null)
        {
            customer.UserId = user.Id;
            customer.FirstName = user.FirstName;
            customer.LastName = user.LastName;
            customer.Phone = user.PhoneNumber ?? string.Empty;
            await _context.SaveChangesAsync(cancellationToken);
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? AppRoles.Customer;
        var permissions = GetPermissionsForRole(role);

        return new AuthResponse
        {
            Success = true,
            Message = "Profile updated successfully",
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.PhoneNumber ?? string.Empty,
                Role = role,
                Permissions = permissions,
                IsActive = user.IsActive,
                RewardPoints = await GetRewardPointsAsync(user.Id, role, cancellationToken)
            }
        };
    }

    public async Task<List<UserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userManager.Users.AsNoTracking().ToListAsync(cancellationToken);
        var list = new List<UserDto>();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? AppRoles.Customer;

            list.Add(new UserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Phone = u.PhoneNumber ?? string.Empty,
                Role = role,
                Permissions = GetPermissionsForRole(role),
                IsActive = u.IsActive
            });
        }

        return list;
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        if (!AppRoles.All.Contains(role))
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        if (!await _roleManager.RoleExistsAsync(role))
        {
            await _roleManager.CreateAsync(new ApplicationRole(role, $"Default {role} role"));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        var result = await _userManager.AddToRoleAsync(user, role);

        return result.Succeeded;
    }

    public async Task<bool> ToggleUserStatusAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        user.IsActive = isActive;
        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<List<LoginHistoryDto>> GetLoginHistoryAsync(CancellationToken cancellationToken = default)
    {
        return await _context.LoginHistories
            .OrderByDescending(h => h.TimestampUtc)
            .Take(100)
            .Select(h => new LoginHistoryDto
            {
                Id = h.Id,
                UserId = h.UserId,
                Email = h.Email,
                TimestampUtc = h.TimestampUtc,
                IpAddress = h.IpAddress,
                UserAgent = h.UserAgent,
                Success = h.Success,
                FailureReason = h.FailureReason
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RateLimitLogDto>> GetRateLimitLogsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RateLimitLogs
            .OrderByDescending(r => r.TimestampUtc)
            .Take(100)
            .Select(r => new RateLimitLogDto
            {
                Id = r.Id,
                TimestampUtc = r.TimestampUtc,
                Endpoint = r.Endpoint,
                Policy = r.Policy,
                IpAddress = r.IpAddress,
                RequestsCount = r.RequestsCount,
                BlockedCount = r.BlockedCount,
                Reason = r.Reason
            })
            .ToListAsync(cancellationToken);
    }


    private const string PasswordResetPurpose = "PasswordReset";

    public async Task<string?> GeneratePasswordResetOtpAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserByIdentifierAsync(identifier, cancellationToken);
        if (user == null || !user.IsActive)
        {
            // Do not leak account existence — caller returns a generic message either way.
            return null;
        }

        // Invalidate any prior active OTPs for this user
        var now = DateTime.UtcNow;
        var activeOtps = await _context.OtpVerifications
            .Where(o => o.UserId == user.Id && o.Purpose == PasswordResetPurpose && o.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var prior in activeOtps)
        {
            prior.ConsumedAtUtc = now;
            prior.ResetToken = null;
            prior.ResetTokenExpiresAtUtc = null;
        }

        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _context.OtpVerifications.Add(new OtpVerification
        {
            UserId = user.Id,
            Code = code,
            Purpose = PasswordResetPurpose,
            ExpiresAtUtc = now.AddMinutes(5)
        });

        await _context.SaveChangesAsync(cancellationToken);

        var recipient = identifier.Contains('@') ? (user.Email ?? identifier) : (user.PhoneNumber ?? identifier);
        await _notificationService.SendPasswordResetOtpAsync(recipient, code, cancellationToken);

        return code;
    }

    public async Task<string?> VerifyPasswordResetOtpAsync(string identifier, string otp, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserByIdentifierAsync(identifier, cancellationToken);
        if (user == null || string.IsNullOrWhiteSpace(otp)) return null;

        var now = DateTime.UtcNow;
        var code = otp.Trim();
        var otpEntry = await _context.OtpVerifications
            .Where(o => o.UserId == user.Id
                        && o.Purpose == PasswordResetPurpose
                        && o.Code == code
                        && o.ConsumedAtUtc == null
                        && o.ExpiresAtUtc > now)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (otpEntry == null) return null;

        // OTP is single-use: consume it and issue a short-lived reset token.
        otpEntry.ConsumedAtUtc = now;
        otpEntry.ResetToken = Guid.NewGuid().ToString("N");
        otpEntry.ResetTokenExpiresAtUtc = now.AddMinutes(10);

        await _context.SaveChangesAsync(cancellationToken);
        return otpEntry.ResetToken;
    }

    public async Task<bool> ResetPasswordWithTokenAsync(string resetToken, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resetToken) || string.IsNullOrWhiteSpace(newPassword)) return false;

        var now = DateTime.UtcNow;
        var token = resetToken.Trim();
        var otpEntry = await _context.OtpVerifications
            .Where(o => o.ResetToken == token
                        && o.Purpose == PasswordResetPurpose
                        && o.ResetTokenExpiresAtUtc != null
                        && o.ResetTokenExpiresAtUtc > now)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (otpEntry == null) return false;

        var user = await _userManager.FindByIdAsync(otpEntry.UserId);
        if (user == null) return false;

        var identityToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, identityToken, newPassword);
        if (!result.Succeeded) return false;

        // Reset token is single-use.
        otpEntry.ResetToken = null;
        otpEntry.ResetTokenExpiresAtUtc = null;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private string GenerateJwtToken(ApplicationUser user, string role, List<string> permissions)
    {
        var secretKey = JwtSecretProvider.Resolve(_configuration); // must match validation key in DependencyInjection
        var issuer = _configuration["JwtSettings:Issuer"] ?? "AadhiCrackers.Api";
        var audience = _configuration["JwtSettings:Audience"] ?? "AadhiCrackers.Clients";
        var expiryMinutes = int.TryParse(_configuration["JwtSettings:ExpiryMinutes"], out var exp) ? exp : 1440; // 24 hours

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        foreach (var perm in permissions)
        {
            claims.Add(new Claim("permission", perm));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static List<string> GetPermissionsForRole(string role)
    {
        return role switch
        {
            AppRoles.SuperAdmin => new List<string>
            {
                AppPermissions.ProductsRead, AppPermissions.ProductsCreate, AppPermissions.ProductsUpdate, AppPermissions.ProductsDelete,
                AppPermissions.OrdersRead, AppPermissions.OrdersCreate, AppPermissions.OrdersUpdate, AppPermissions.OrdersCancel,
                AppPermissions.InventoryRead, AppPermissions.InventoryAdjust,
                AppPermissions.PurchasesRead, AppPermissions.PurchasesCreate,
                AppPermissions.InvoicesRead, AppPermissions.InvoicesCreate,
                AppPermissions.ReportsRead, AppPermissions.AuditLogsRead,
                AppPermissions.UsersRead, AppPermissions.UsersManage, AppPermissions.SettingsManage
            },
            AppRoles.Admin => new List<string>
            {
                AppPermissions.ProductsRead, AppPermissions.ProductsCreate, AppPermissions.ProductsUpdate,
                AppPermissions.OrdersRead, AppPermissions.OrdersCreate, AppPermissions.OrdersUpdate, AppPermissions.OrdersCancel,
                AppPermissions.InventoryRead, AppPermissions.InventoryAdjust,
                AppPermissions.PurchasesRead, AppPermissions.PurchasesCreate,
                AppPermissions.InvoicesRead, AppPermissions.InvoicesCreate,
                AppPermissions.ReportsRead, AppPermissions.AuditLogsRead,
                AppPermissions.UsersRead
            },
            AppRoles.InventoryManager => new List<string>
            {
                AppPermissions.ProductsRead, AppPermissions.InventoryRead, AppPermissions.InventoryAdjust,
                AppPermissions.PurchasesRead, AppPermissions.PurchasesCreate
            },
            AppRoles.SalesExecutive => new List<string>
            {
                AppPermissions.ProductsRead, AppPermissions.OrdersRead, AppPermissions.OrdersCreate, AppPermissions.OrdersUpdate
            },
            AppRoles.Accountant => new List<string>
            {
                AppPermissions.InvoicesRead, AppPermissions.InvoicesCreate, AppPermissions.ReportsRead
            },
            _ => new List<string>
            {
                AppPermissions.ProductsRead,
                AppPermissions.OrdersCreate,
                AppPermissions.OrdersRead
            }
        };
    }
}
