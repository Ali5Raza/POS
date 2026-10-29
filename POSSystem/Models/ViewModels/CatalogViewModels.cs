using System.ComponentModel.DataAnnotations;
using POSSystem.Models.DTOs;
using POSSystem.Models.Entities;

namespace POSSystem.Models.ViewModels;

public class ProductCreateViewModel
{
    [Required(ErrorMessage = "SKU is required"), MaxLength(60)]
    public string SKU { get; set; } = string.Empty;

    [Required(ErrorMessage = "Barcode is required"), MaxLength(80)]
    public string Barcode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Product name is required"), MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select a category")]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a valid category")]
    public int CategoryId { get; set; }

    public int? DefaultSupplierId { get; set; }

    [Required, Range(0, 999999999, ErrorMessage = "Cost price must be non-negative")]
    public decimal CostPrice { get; set; }

    [Required, Range(0.01, 999999999, ErrorMessage = "Sale price must be greater than zero")]
    public decimal SalePrice { get; set; }

    [Range(0, 999999999)]
    public decimal WholesalePrice { get; set; }

    [Range(0, 999999999)]
    public decimal MinimumSalePrice { get; set; }

    [Range(0, 100)]
    public decimal TaxRate { get; set; }

    [Required, MaxLength(30)]
    public string Unit { get; set; } = "pcs";

    [Range(0, 999999999, ErrorMessage = "Opening stock cannot be negative")]
    public decimal OpeningStock { get; set; }

    [Range(0, 999999999)]
    public decimal MinimumStockLevel { get; set; } = 5;

    [Range(0, 999999999)]
    public decimal ReorderLevel { get; set; } = 15;

    public bool IsActive { get; set; } = true;
}

public class ProductEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "SKU is required"), MaxLength(60)]
    public string SKU { get; set; } = string.Empty;

    [Required(ErrorMessage = "Barcode is required"), MaxLength(80)]
    public string Barcode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Product name is required"), MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, Range(1, int.MaxValue, ErrorMessage = "Please select a valid category")]
    public int CategoryId { get; set; }

    public int? DefaultSupplierId { get; set; }

    [Required, Range(0, 999999999)]
    public decimal CostPrice { get; set; }

    [Required, Range(0.01, 999999999)]
    public decimal SalePrice { get; set; }

    [Range(0, 999999999)]
    public decimal WholesalePrice { get; set; }

    [Range(0, 999999999)]
    public decimal MinimumSalePrice { get; set; }

    [Range(0, 100)]
    public decimal TaxRate { get; set; }

    [Required, MaxLength(30)]
    public string Unit { get; set; } = "pcs";

    public decimal CurrentStock { get; set; }

    [Range(0, 999999999)]
    public decimal MinimumStockLevel { get; set; }

    [Range(0, 999999999)]
    public decimal ReorderLevel { get; set; }

    public bool IsActive { get; set; } = true;
}

public class CategoryViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Category Name is required"), MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }

    public bool IsActive { get; set; } = true;
    public int ProductCount { get; set; }
}

public class SupplierViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Supplier Contact Name is required"), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Company Name is required"), MaxLength(180)]
    public string CompanyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone is required"), MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress, MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(80)]
    public string? TaxNumber { get; set; }

    [Range(0, 999999999)]
    public decimal OpeningBalance { get; set; }

    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class SupplierDetailsViewModel
{
    public Supplier Supplier { get; set; } = null!;
    public List<GoodsReceipt> Purchases { get; set; } = new();
    public List<PurchaseReturn> PurchaseReturns { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<LedgerEntryDto> Ledger { get; set; } = new();
}

public class CustomerViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Customer Name is required"), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [EmailAddress, MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [Range(0, 999999999)]
    public decimal OpeningBalance { get; set; }

    [Range(0, 999999999)]
    public decimal CreditLimit { get; set; } = 25000m;

    public decimal CurrentBalance { get; set; }
    public bool IsWalkIn { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomerDetailsViewModel
{
    public Customer Customer { get; set; } = null!;
    public List<Sale> Sales { get; set; } = new();
    public List<SalesReturn> SalesReturns { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<LedgerEntryDto> Ledger { get; set; } = new();
}
