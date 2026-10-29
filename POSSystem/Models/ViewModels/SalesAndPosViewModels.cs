using System.ComponentModel.DataAnnotations;
using POSSystem.Models.DTOs;
using POSSystem.Models.Entities;

namespace POSSystem.Models.ViewModels;

public class POSViewModel
{
    public List<ProductCategory> Categories { get; set; } = new();
    public List<ProductLookupDto> Products { get; set; } = new();
    public List<Customer> Customers { get; set; } = new();
    public List<HeldSale> HeldSales { get; set; } = new();
    public Customer DefaultWalkInCustomer { get; set; } = null!;
    public bool CanOverridePrice { get; set; }
    public bool CanApplyHighDiscount { get; set; }
    public bool AllowNegativeStock { get; set; }
    public decimal MaxNormalDiscountPercent { get; set; } = 20m;
    public bool EnableTax { get; set; } = true;
    public decimal DefaultTaxRate { get; set; } = 5m;
    public string DefaultPaymentMethod { get; set; } = "Cash";
}

public class POSCartItemViewModel
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? SKU { get; set; }

    [Range(0.01, 999999999, ErrorMessage = "Quantity must be positive")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999)]
    public decimal UnitPrice { get; set; }

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 100)]
    public decimal TaxRate { get; set; }
}

public class SaleViewModel
{
    public int? CustomerId { get; set; }
    public string PriceTier { get; set; } = "Retail"; // Retail, Wholesale

    [Range(0, 999999999)]
    public decimal InvoiceDiscountAmount { get; set; }

    [Range(0, 999999999)]
    public decimal PaidAmount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, Bank Transfer, Credit

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? HeldSaleId { get; set; }

    public List<SaleItemViewModel> Items { get; set; } = new();
}

public class SaleItemViewModel
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? SKU { get; set; }

    [Range(0.01, 999999999)]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 999999999)]
    public decimal UnitPrice { get; set; }

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 100)]
    public decimal TaxRate { get; set; }
}

public class HoldSaleViewModel
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string CartJson { get; set; } = "[]";
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
}

public class SalesReturnViewModel
{
    [Required]
    public int SaleId { get; set; }
    public string? InvoiceNumber { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [Required(ErrorMessage = "Return reason is required"), MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string RefundMethod { get; set; } = "Cash"; // Cash, Card, Bank Transfer, CustomerCredit

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<SalesReturnItemViewModel> Items { get; set; } = new();
}

public class SalesReturnItemViewModel
{
    public int SaleItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal SoldQuantity { get; set; }
    public decimal AlreadyReturnedQuantity { get; set; }
    public decimal MaxReturnableQuantity => Math.Max(0, SoldQuantity - AlreadyReturnedQuantity);
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }

    [Range(0, 999999999)]
    public decimal ReturnQuantity { get; set; }

    [MaxLength(250)]
    public string? ItemReason { get; set; }
}

public class ReceiptViewModel
{
    public Sale Sale { get; set; } = null!;
    public string BusinessName { get; set; } = "Apex Retail Mart & Superstore";
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPhone { get; set; } = string.Empty;
    public string BusinessEmail { get; set; } = string.Empty;
    public string BusinessLogo { get; set; } = "/images/logo.svg";
    public string TaxNumber { get; set; } = string.Empty;
    public string InvoiceFooter { get; set; } = string.Empty;
    public string Format { get; set; } = "Thermal"; // Thermal, A4
    public bool AutoPrint { get; set; }
}
