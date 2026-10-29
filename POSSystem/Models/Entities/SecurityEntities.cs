using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models.Entities;

public class ApplicationUser
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}

public class Role
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Description { get; set; }

    public bool IsSystemRole { get; set; } = false;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}

public class Permission
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Module { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    public int Id { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }

    [MaxLength(100)]
    public string Username { get; set; } = "System";

    [Required, MaxLength(120)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string EntityName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? EntityId { get; set; }

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(80)]
    public string? IpAddress { get; set; }
}

public class SystemSetting
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Group { get; set; } = "General";

    [MaxLength(250)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class PermissionCodes
{
    public const string ViewProducts = "ViewProducts";
    public const string CreateProduct = "CreateProduct";
    public const string EditProduct = "EditProduct";
    public const string DeleteProduct = "DeleteProduct";

    public const string ViewPurchases = "ViewPurchases";
    public const string CreatePurchase = "CreatePurchase";
    public const string ApprovePurchase = "ApprovePurchase";
    public const string ReceiveGoods = "ReceiveGoods";
    public const string PurchaseReturn = "PurchaseReturn";

    public const string ViewSales = "ViewSales";
    public const string CreateSale = "CreateSale";
    public const string CancelSale = "CancelSale";
    public const string SalesReturn = "SalesReturn";

    public const string ApplyDiscount = "ApplyDiscount";
    public const string OverridePrice = "OverridePrice";

    public const string ViewInventory = "ViewInventory";
    public const string AdjustInventory = "AdjustInventory";

    public const string ViewPayments = "ViewPayments";
    public const string CreatePayment = "CreatePayment";

    public const string ViewExpenses = "ViewExpenses";
    public const string CreateExpense = "CreateExpense";

    public const string ViewReports = "ViewReports";
    public const string ExportReports = "ExportReports";

    public const string ManageUsers = "ManageUsers";
    public const string ManageRoles = "ManageRoles";
    public const string ManageSettings = "ManageSettings";

    public const string DayClosing = "DayClosing";
    public const string MonthlyClosing = "MonthlyClosing";

    public const string ViewAuditLogs = "ViewAuditLogs";
}
