using System.ComponentModel.DataAnnotations;
using POSSystem.Models.Entities;

namespace POSSystem.Models.ViewModels;

public class StockAdjustmentViewModel
{
    [Required(ErrorMessage = "Please select a product")]
    [Range(1, int.MaxValue, ErrorMessage = "Please select a product")]
    public int ProductId { get; set; }

    [Required, MaxLength(30)]
    public string AdjustmentType { get; set; } = "Increase"; // Increase, Decrease

    [Required, Range(0.01, 999999999, ErrorMessage = "Adjustment quantity must be greater than zero")]
    public decimal Quantity { get; set; }

    [Required(ErrorMessage = "Reason is required"), MaxLength(250)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Notes { get; set; }
}

public class PaymentViewModel
{
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(50)]
    public string PaymentType { get; set; } = "CustomerReceipt";
    // CustomerReceipt, SupplierPayment, SaleReceipt, PurchasePayment, SalesRefund, PurchaseRefund

    [Required, Range(0.01, 999999999, ErrorMessage = "Payment amount must be greater than zero")]
    public decimal Amount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, Bank Transfer, Other

    [MaxLength(80)]
    public string? ReferenceNumber { get; set; }

    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public int? SaleId { get; set; }
    public int? GoodsReceiptId { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }
}

public class ExpenseViewModel
{
    public int Id { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

    [Required(ErrorMessage = "Please select an expense category")]
    [Range(1, int.MaxValue, ErrorMessage = "Please select an expense category")]
    public int ExpenseCategoryId { get; set; }

    [Required(ErrorMessage = "Description is required"), MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    [Required, Range(0.01, 999999999, ErrorMessage = "Expense amount must be greater than zero")]
    public decimal Amount { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethod { get; set; } = "Cash";

    [MaxLength(80)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }
}

public class ExpenseCategoryViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Category Name is required"), MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}

public class DayClosingViewModel
{
    public DateTime ClosingDate { get; set; } = DateTime.UtcNow.Date;

    [Range(0, 999999999)]
    public decimal OpeningCash { get; set; }

    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal BankSales { get; set; }
    public decimal CustomerPayments { get; set; }
    public decimal Expenses { get; set; }
    public decimal Refunds { get; set; }
    public decimal ExpectedCash { get; set; }

    [Range(0, 999999999)]
    public decimal ActualCash { get; set; }

    public decimal Difference => ActualCash - ExpectedCash;
    public int TotalTransactions { get; set; }
    public bool IsAlreadyClosed { get; set; }
    public DayClosing? ExistingRecord { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }

    public List<DayClosing> PastClosings { get; set; } = new();
}

public class MonthlyClosingViewModel
{
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public int Month { get; set; } = DateTime.UtcNow.Month;

    public decimal TotalSales { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal SalesReturns { get; set; }
    public decimal PurchaseReturns { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal NetProfit { get; set; }
    public decimal Receivables { get; set; }
    public decimal Payables { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal CashSummary { get; set; }

    public bool IsAlreadyClosed { get; set; }
    public MonthlyClosing? ExistingRecord { get; set; }

    [MaxLength(400)]
    public string? Notes { get; set; }

    public List<MonthlyClosing> PastClosings { get; set; } = new();
}
