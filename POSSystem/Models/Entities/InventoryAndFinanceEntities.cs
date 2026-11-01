using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models.Entities;

public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; } = 1;
    public Warehouse? Warehouse { get; set; }

    [Required, MaxLength(50)]
    public string TransactionType { get; set; } = string.Empty;
    // Opening Stock, Purchase, Purchase Return, Sale, Sales Return, Adjustment In, Adjustment Out, Transfer In, Transfer Out

    [Required, MaxLength(60)]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal QuantityIn { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QuantityOut { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    public int? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StockAdjustment
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string AdjustmentNumber { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WarehouseId { get; set; } = 1;
    public Warehouse? Warehouse { get; set; }

    [Required, MaxLength(30)]
    public string AdjustmentType { get; set; } = "Increase"; // Increase, Decrease

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PreviousQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [Required, MaxLength(250)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Notes { get; set; }

    public int? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Payment
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PaymentNumber { get; set; } = string.Empty;

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(50)]
    public string PaymentType { get; set; } = "SaleReceipt";
    // SaleReceipt, CustomerReceipt, PurchasePayment, SupplierPayment, SalesRefund, PurchaseRefund

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, Bank Transfer, Other

    [MaxLength(80)]
    public string? ReferenceNumber { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public int? SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }

    public int? PurchaseReturnId { get; set; }
    public PurchaseReturn? PurchaseReturn { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }

    public int? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ExpenseCategory
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

public class Expense
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ExpenseNumber { get; set; } = string.Empty;

    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }

    [Required, MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash";

    [MaxLength(80)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }

    public int? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DayClosing
{
    public int Id { get; set; }

    public DateTime ClosingDate { get; set; } // Date-normalized (UTC/Local date midnight)

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CardSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal BankSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CustomerPayments { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Expenses { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Refunds { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ExpectedCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ActualCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Difference { get; set; }

    public int TotalTransactions { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "Closed"; // Closed, Reopened

    [MaxLength(400)]
    public string? Notes { get; set; }

    public int? ClosedByUserId { get; set; }
    public ApplicationUser? ClosedByUser { get; set; }

    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;

    public int? ReopenedByUserId { get; set; }
    public ApplicationUser? ReopenedByUser { get; set; }
    public DateTime? ReopenedAt { get; set; }
}

public class MonthlyClosing
{
    public int Id { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPurchases { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SalesReturns { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchaseReturns { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalExpenses { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrossProfit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NetProfit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Receivables { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Payables { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal InventoryValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashSummary { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "Closed"; // Closed, Reopened

    [MaxLength(400)]
    public string? Notes { get; set; }

    public int? ClosedByUserId { get; set; }
    public ApplicationUser? ClosedByUser { get; set; }

    public DateTime ClosingDate { get; set; } = DateTime.UtcNow;

    public int? ReopenedByUserId { get; set; }
    public ApplicationUser? ReopenedByUser { get; set; }
    public DateTime? ReopenedAt { get; set; }
}
