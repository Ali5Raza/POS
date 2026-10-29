using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models.Entities;

public class Sale
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime SaleDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public bool IsWalkIn { get; set; } = true;

    public int? CashierId { get; set; }
    public ApplicationUser? Cashier { get; set; }

    public int WarehouseId { get; set; } = 1;
    public Warehouse? Warehouse { get; set; }

    [MaxLength(30)]
    public string PriceTier { get; set; } = "Retail"; // Retail, Wholesale

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ChangeAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DueAmount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, BankTransfer, Credit, Split

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Completed"; // Completed, Cancelled, Returned, PartiallyReturned

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<SalesReturn> SalesReturns { get; set; } = new List<SalesReturn>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class SaleItem
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReturnedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OriginalUnitPrice { get; set; }

    public bool IsPriceOverridden { get; set; } = false;

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxRate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}

public class HeldSale
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string HoldReference { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    [MaxLength(150)]
    public string CustomerName { get; set; } = "Walk-in Customer";

    [Required]
    public string CartJson { get; set; } = "[]";

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SalesReturn
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ReturnNumber { get; set; } = string.Empty;

    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxRefund { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalRefundAmount { get; set; }

    [Required, MaxLength(40)]
    public string RefundMethod { get; set; } = "Cash"; // Cash, Card, BankTransfer, CustomerCredit

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidRefundAmount { get; set; }

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Completed"; // Completed, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SalesReturnItem> Items { get; set; } = new List<SalesReturnItem>();
}

public class SalesReturnItem
{
    public int Id { get; set; }

    public int SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }

    public int SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReturnQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    [MaxLength(250)]
    public string? Reason { get; set; }
}
