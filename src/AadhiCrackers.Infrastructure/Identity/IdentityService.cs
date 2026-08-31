using AadhiCrackers.Application.Common.Interfaces;
using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _context;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
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
            Token = Guid.NewGuid().ToString("N") // Session token
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

        var userDto = new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber,
            Role = AppRoles.Customer,
            Permissions = GetPermissionsForRole(AppRoles.Customer),
            IsActive = true
        };

        return new AuthResponse
        {
            Success = true,
            Message = "Registration successful",
            User = userDto,
            Token = Guid.NewGuid().ToString("N")
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
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, role);

        return true;
    }

    public async Task<bool> ToggleUserStatusAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        user.IsActive = isActive;
        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
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
                AppPermissions.ProductsRead
            }
        };
    }
}
