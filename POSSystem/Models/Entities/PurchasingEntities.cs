using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models.Entities;

public class PurchaseOrder
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PONumber { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Draft"; // Draft, Ordered, PartiallyReceived, FullyReceived, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}

public class PurchaseOrderItem
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OrderedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceivedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}

public class GoodsReceipt
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string GRNNumber { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int WarehouseId { get; set; } = 1;
    public Warehouse? Warehouse { get; set; }

    public DateTime ReceivingDate { get; set; } = DateTime.UtcNow;

    [MaxLength(80)]
    public string? SupplierInvoiceNumber { get; set; }

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
    public decimal DueAmount { get; set; }

    [MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash";

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Completed"; // Completed, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
    public ICollection<PurchaseReturn> PurchaseReturns { get; set; } = new List<PurchaseReturn>();
}

public class GoodsReceiptItem
{
    public int Id { get; set; }

    public int GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public int? PurchaseOrderItemId { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OrderedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceivedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DamagedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReturnedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}

public class PurchaseReturn
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ReturnNumber { get; set; } = string.Empty;

    public int GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalReturnAmount { get; set; }

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Completed"; // Completed, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}

public class PurchaseReturnItem
{
    public int Id { get; set; }

    public int PurchaseReturnId { get; set; }
    public PurchaseReturn? PurchaseReturn { get; set; }

    public int GoodsReceiptItemId { get; set; }
    public GoodsReceiptItem? GoodsReceiptItem { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReturnQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    [MaxLength(250)]
    public string? Reason { get; set; }
}
