using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AadhiCrackers.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService? _currentUser;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext context,
        IConfiguration configuration,
        ICurrentUserService? currentUser = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
        _configuration = configuration;
        _currentUser = currentUser;
    }

    public async Task<AuthResponse> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
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
                Email = request.Email,
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
            IsActive = user.IsActive
        };

        return new AuthResponse
        {
            Success = true,
            Message = "Login successful",
            User = userDto,
            Token = token
        };
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
            IsActive = user.IsActive
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
        var validRoles = new[] { AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.InventoryManager, AppRoles.SalesExecutive, AppRoles.Accountant, AppRoles.Customer };
        if (!validRoles.Contains(role))
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

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


    private string GenerateJwtToken(ApplicationUser user, string role, List<string> permissions)
    {
        var secretKey = _configuration["JwtSettings:SecretKey"] ?? "AadhiCrackers_Secure_Enterprise_JWT_Secret_Key_2026_!@#$999";
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
