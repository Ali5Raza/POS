namespace POSSystem.Models.DTOs;

public class ProductLookupDto
{
    public int Id { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal WholesalePrice { get; set; }
    public decimal MinimumSalePrice { get; set; }
    public decimal TaxRate { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal CurrentStock { get; set; }
    public decimal MinimumStockLevel { get; set; }
}

public class LedgerEntryDto
{
    public DateTime Date { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

public class DailyChartPointDto
{
    public string DateLabel { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Purchases { get; set; }
    public decimal Profit { get; set; }
}

public class TopItemChartDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal Quantity { get; set; }
}
