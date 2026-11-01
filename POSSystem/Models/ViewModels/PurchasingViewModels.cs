using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models.ViewModels;

public class PurchaseCreateViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please select a supplier")]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a supplier")]
    public int SupplierId { get; set; }

    public DateTime OrderDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? ExpectedDeliveryDate { get; set; } = DateTime.UtcNow.Date.AddDays(3);

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 999999999)]
    public decimal TaxAmount { get; set; }

    [MaxLength(40)]
    public string Status { get; set; } = "Ordered"; // Draft, Ordered

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<PurchaseItemViewModel> Items { get; set; } = new();
}

public class PurchaseItemViewModel
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? SKU { get; set; }

    [Range(0.01, 999999999, ErrorMessage = "Quantity must be greater than zero")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 999999999)]
    public decimal CostPrice { get; set; }

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 999999999)]
    public decimal TaxAmount { get; set; }
}

public class GoodsReceiptViewModel
{
    public int? PurchaseOrderId { get; set; }
    public string? PONumber { get; set; }

    [Required(ErrorMessage = "Supplier is required")]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a supplier")]
    public int SupplierId { get; set; }

    public DateTime ReceivingDate { get; set; } = DateTime.UtcNow;

    [MaxLength(80)]
    public string? SupplierInvoiceNumber { get; set; }

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 999999999)]
    public decimal TaxAmount { get; set; }

    [Range(0, 999999999)]
    public decimal PaidAmount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash";

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<GoodsReceiptItemViewModel> Items { get; set; } = new();
}

public class GoodsReceiptItemViewModel
{
    public int? PurchaseOrderItemId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? SKU { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyReceivedQuantity { get; set; }

    [Range(0, 999999999)]
    public decimal ReceivedQuantity { get; set; }

    [Range(0, 999999999)]
    public decimal DamagedQuantity { get; set; }

    [Range(0, 999999999)]
    public decimal CostPrice { get; set; }

    [Range(0, 999999999)]
    public decimal DiscountAmount { get; set; }

    [Range(0, 999999999)]
    public decimal TaxAmount { get; set; }
}

public class PurchaseReturnViewModel
{
    [Required]
    public int GoodsReceiptId { get; set; }
    public string? GRNNumber { get; set; }
    public int SupplierId { get; set; }
    public string? SupplierName { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [Required(ErrorMessage = "Return reason is required"), MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<PurchaseReturnItemViewModel> Items { get; set; } = new();
}

public class PurchaseReturnItemViewModel
{
    public int GoodsReceiptItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal AlreadyReturnedQuantity { get; set; }
    public decimal MaxReturnableQuantity => Math.Max(0, ReceivedQuantity - AlreadyReturnedQuantity);
    public decimal UnitCost { get; set; }

    [Range(0, 999999999)]
    public decimal ReturnQuantity { get; set; }

    [MaxLength(250)]
    public string? ItemReason { get; set; }
}
