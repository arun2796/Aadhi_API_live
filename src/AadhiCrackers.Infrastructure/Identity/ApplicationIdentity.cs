using Microsoft.AspNetCore.Identity;

namespace AadhiCrackers.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? CustomerCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }

    public ApplicationRole() { }
    public ApplicationRole(string roleName, string? description = null) : base(roleName)
    {
        Description = description;
    }
}

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string SalesExecutive = "SalesExecutive";
    public const string InventoryManager = "InventoryManager";
    public const string PurchaseManager = "PurchaseManager";
    public const string Accountant = "Accountant";
    public const string SupportAgent = "SupportAgent";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SuperAdmin, Admin, Manager, SalesExecutive,
        InventoryManager, PurchaseManager, Accountant,
        SupportAgent, Customer
    };
}

public static class AppPermissions
{
    public const string ProductsRead = "Products.Read";
    public const string ProductsCreate = "Products.Create";
    public const string ProductsUpdate = "Products.Update";
    public const string ProductsDelete = "Products.Delete";

    public const string OrdersRead = "Orders.Read";
    public const string OrdersCreate = "Orders.Create";
    public const string OrdersUpdate = "Orders.Update";
    public const string OrdersCancel = "Orders.Cancel";

    public const string InventoryRead = "Inventory.Read";
    public const string InventoryAdjust = "Inventory.Adjust";

    public const string PurchasesRead = "Purchases.Read";
    public const string PurchasesCreate = "Purchases.Create";

    public const string InvoicesRead = "Invoices.Read";
    public const string InvoicesCreate = "Invoices.Create";

    public const string ReportsRead = "Reports.Read";
    public const string AuditLogsRead = "AuditLogs.Read";

    public const string UsersRead = "Users.Read";
    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
}
