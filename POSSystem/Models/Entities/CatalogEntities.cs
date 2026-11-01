using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models.Entities;

public class Warehouse
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Code { get; set; } = "MAIN";

    [Required, MaxLength(120)]
    public string Name { get; set; } = "Main Store & Warehouse";

    [MaxLength(250)]
    public string? Address { get; set; }

    public bool IsDefault { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProductCategory
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }
    public ProductCategory? ParentCategory { get; set; }

    public ICollection<ProductCategory> SubCategories { get; set; } = new List<ProductCategory>();
    public ICollection<Product> Products { get; set; } = new List<Product>();

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Supplier
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(180)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(80)]
    public string? TaxNumber { get; set; } // NTN/CNIC optional

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
    public ICollection<PurchaseReturn> PurchaseReturns { get; set; } = new List<PurchaseReturn>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Customer
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CreditLimit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentBalance { get; set; }

    public bool IsWalkIn { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<SalesReturn> SalesReturns { get; set; } = new List<SalesReturn>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Product
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string SKU { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Barcode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public ProductCategory? Category { get; set; }

    public int? DefaultSupplierId { get; set; }
    public Supplier? DefaultSupplier { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal WholesalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinimumSalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxRate { get; set; }

    [Required, MaxLength(30)]
    public string Unit { get; set; } = "pcs";

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningStock { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentStock { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinimumStockLevel { get; set; } = 5;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReorderLevel { get; set; } = 10;

    [MaxLength(250)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
