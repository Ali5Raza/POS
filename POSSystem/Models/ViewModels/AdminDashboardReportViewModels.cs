using System.ComponentModel.DataAnnotations;
using POSSystem.Models.DTOs;
using POSSystem.Models.Entities;

namespace POSSystem.Models.ViewModels;

public class DashboardViewModel
{
    // Today's Summary
    public decimal TodaySales { get; set; }
    public decimal TodayPurchases { get; set; }
    public decimal TodayGrossProfit { get; set; }
    public decimal TodayExpenses { get; set; }
    public decimal TodayNetProfit { get; set; }
    public decimal TodayCashSales { get; set; }
    public decimal TodayCreditSales { get; set; }
    public decimal TodayReturns { get; set; }
    public int TodayTransactions { get; set; }

    // Inventory Summary
    public int TotalProducts { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public decimal InventoryCostValue { get; set; }
    public decimal InventoryRetailValue { get; set; }

    // Charts
    public List<DailyChartPointDto> Last7DaysChart { get; set; } = new();
    public List<TopItemChartDto> PaymentMethodsChart { get; set; } = new();
    public List<TopItemChartDto> TopProductsChart { get; set; } = new();
    public List<TopItemChartDto> TopCategoriesChart { get; set; } = new();

    // Recent lists
    public List<Sale> RecentSales { get; set; } = new();
    public List<Product> LowStockProducts { get; set; } = new();
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare("NewPassword", ErrorMessage = "Passwords do not match")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UserCreateViewModel
{
    [Required, MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UserEditViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MinLength(6)]
    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    [Required]
    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;
}

public class RoleEditViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Description { get; set; }

    public bool IsSystemRole { get; set; }
    public List<int> SelectedPermissionIds { get; set; } = new();
    public List<Permission> AllPermissions { get; set; } = new();
}

public class SettingsViewModel
{
    // Business
    [Required, MaxLength(150)]
    public string BusinessName { get; set; } = "Apex Retail Mart & Superstore";
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPhone { get; set; } = string.Empty;
    public string BusinessEmail { get; set; } = string.Empty;
    public string BusinessLogo { get; set; } = "/images/logo.svg";
    public string TaxNumber { get; set; } = string.Empty;
    public string InvoiceFooter { get; set; } = string.Empty;

    // POS
    public string DefaultCustomer { get; set; } = "Walk-in Customer";
    public string DefaultPaymentMethod { get; set; } = "Cash";
    public bool AutoPrint { get; set; }
    public string ReceiptFormat { get; set; } = "Thermal";
    public bool AllowNegativeStock { get; set; }
    public decimal MaximumDiscountPercent { get; set; } = 20m;
    public bool RequireCustomerForCreditSale { get; set; } = true;

    // Inventory
    public decimal DefaultMinimumStock { get; set; } = 5m;
    public decimal DefaultReorderLevel { get; set; } = 15m;
    public string NegativeStockPolicy { get; set; } = "Block";

    // Tax
    public bool EnableTax { get; set; } = true;
    public decimal DefaultTaxRate { get; set; } = 5m;

    // Numbering
    public string Prefix_INV { get; set; } = "INV";
    public string Prefix_PO { get; set; } = "PO";
    public string Prefix_GRN { get; set; } = "GRN";
    public string Prefix_PR { get; set; } = "PR";
    public string Prefix_SR { get; set; } = "SR";
    public string Prefix_PAY { get; set; } = "PAY";
    public string Prefix_EXP { get; set; } = "EXP";
}

public class ReportFilterViewModel
{
    public string ReportType { get; set; } = "DailySales";
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date.AddDays(-30);
    public DateTime EndDate { get; set; } = DateTime.UtcNow.Date;
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? SupplierId { get; set; }
    public int? CustomerId { get; set; }
    public int? CashierId { get; set; }
    public string? PaymentMethod { get; set; }
}
