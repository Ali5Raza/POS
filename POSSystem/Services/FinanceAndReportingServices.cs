using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.DTOs;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface IPaymentService
{
    IQueryable<Payment> QueryPayments(string? search = null, string? paymentType = null, string? paymentMethod = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<Payment> CreatePaymentAsync(PaymentViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
}

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public PaymentService(
        ApplicationDbContext context,
        ISettingsService settingsService,
        IDayClosingService dayClosingService,
        IAuditService auditService)
    {
        _context = context;
        _settingsService = settingsService;
        _dayClosingService = dayClosingService;
        _auditService = auditService;
    }

    public IQueryable<Payment> QueryPayments(string? search = null, string? paymentType = null, string? paymentMethod = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Include(p => p.Sale)
            .Include(p => p.GoodsReceipt)
            .Include(p => p.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(paymentType))
            query = query.Where(p => p.PaymentType == paymentType);

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            query = query.Where(p => p.PaymentMethod == paymentMethod);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p =>
                p.PaymentNumber.ToLower().Contains(s) ||
                (p.ReferenceNumber != null && p.ReferenceNumber.ToLower().Contains(s)) ||
                (p.Customer != null && p.Customer.Name.ToLower().Contains(s)) ||
                (p.Supplier != null && p.Supplier.CompanyName.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(p => p.PaymentDate >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(p => p.PaymentDate < end);
        }

        return query.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id);
    }

    public async Task<Payment> CreatePaymentAsync(PaymentViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(model.PaymentDate, isAdmin);

        if (model.Amount <= 0)
            throw new InvalidOperationException("Payment amount must be greater than zero.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var seq = await _context.Payments.CountAsync() + 1;
            var payNum = await _settingsService.GenerateDocumentNumberAsync("Prefix_PAY", "PAY", seq);

            var payment = new Payment
            {
                PaymentNumber = payNum,
                PaymentDate = model.PaymentDate,
                PaymentType = model.PaymentType,
                Amount = model.Amount,
                PaymentMethod = model.PaymentMethod,
                ReferenceNumber = model.ReferenceNumber?.Trim(),
                CustomerId = model.CustomerId,
                SupplierId = model.SupplierId,
                SaleId = model.SaleId,
                GoodsReceiptId = model.GoodsReceiptId,
                Notes = model.Notes?.Trim(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            if (model.PaymentType == "CustomerReceipt" || model.PaymentType == "SaleReceipt")
            {
                if (!model.CustomerId.HasValue || model.CustomerId.Value <= 0)
                    throw new InvalidOperationException("Please select a customer for Customer Receipt.");

                var customer = await _context.Customers.FindAsync(model.CustomerId.Value)
                    ?? throw new InvalidOperationException("Customer not found.");

                customer.CurrentBalance -= model.Amount;

                if (model.SaleId.HasValue && model.SaleId.Value > 0)
                {
                    var sale = await _context.Sales.FindAsync(model.SaleId.Value);
                    if (sale != null)
                    {
                        sale.PaidAmount += model.Amount;
                        sale.DueAmount = Math.Max(0, sale.TotalAmount - sale.PaidAmount);
                    }
                }
            }
            else if (model.PaymentType == "SupplierPayment" || model.PaymentType == "PurchasePayment")
            {
                if (!model.SupplierId.HasValue || model.SupplierId.Value <= 0)
                    throw new InvalidOperationException("Please select a supplier for Supplier Payment.");

                var supplier = await _context.Suppliers.FindAsync(model.SupplierId.Value)
                    ?? throw new InvalidOperationException("Supplier not found.");

                supplier.CurrentBalance -= model.Amount;

                if (model.GoodsReceiptId.HasValue && model.GoodsReceiptId.Value > 0)
                {
                    var grn = await _context.GoodsReceipts.FindAsync(model.GoodsReceiptId.Value);
                    if (grn != null)
                    {
                        grn.PaidAmount += model.Amount;
                        grn.DueAmount = Math.Max(0, grn.TotalAmount - grn.PaidAmount);
                    }
                }
            }
            else if (model.PaymentType == "SalesRefund" && model.CustomerId.HasValue)
            {
                var customer = await _context.Customers.FindAsync(model.CustomerId.Value);
                if (customer != null && !customer.IsWalkIn)
                {
                    customer.CurrentBalance += model.Amount;
                }
            }
            else if (model.PaymentType == "PurchaseRefund" && model.SupplierId.HasValue)
            {
                var supplier = await _context.Suppliers.FindAsync(model.SupplierId.Value);
                if (supplier != null)
                {
                    supplier.CurrentBalance += model.Amount;
                }
            }

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "CreatePayment", "Payment", payment.Id.ToString(),
                null, $"Recorded {payment.PaymentNumber} ({payment.PaymentType}): {payment.Amount:N2} via {payment.PaymentMethod}", ipAddress);

            await tx.CommitAsync();
            return payment;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}

public interface IExpenseService
{
    IQueryable<Expense> QueryExpenses(string? search = null, int? categoryId = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<ExpenseCategory>> GetCategoriesAsync(bool activeOnly = false);
    Task<Expense> CreateExpenseAsync(ExpenseViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
    Task<ExpenseCategory> CreateCategoryAsync(ExpenseCategoryViewModel model, int? userId, string username, string? ipAddress = null);
}

public class ExpenseService : IExpenseService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public ExpenseService(
        ApplicationDbContext context,
        ISettingsService settingsService,
        IDayClosingService dayClosingService,
        IAuditService auditService)
    {
        _context = context;
        _settingsService = settingsService;
        _dayClosingService = dayClosingService;
        _auditService = auditService;
    }

    public IQueryable<Expense> QueryExpenses(string? search = null, int? categoryId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Expenses
            .AsNoTracking()
            .Include(e => e.ExpenseCategory)
            .Include(e => e.User)
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value > 0)
            query = query.Where(e => e.ExpenseCategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(e =>
                e.ExpenseNumber.ToLower().Contains(s) ||
                e.Description.ToLower().Contains(s) ||
                (e.ReferenceNumber != null && e.ReferenceNumber.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(e => e.ExpenseDate >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(e => e.ExpenseDate < end);
        }

        return query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id);
    }

    public async Task<List<ExpenseCategory>> GetCategoriesAsync(bool activeOnly = false)
    {
        var q = _context.ExpenseCategories.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);
        return await q.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Expense> CreateExpenseAsync(ExpenseViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(model.ExpenseDate, isAdmin);

        if (model.Amount <= 0)
            throw new InvalidOperationException("Expense amount must be greater than zero.");

        var seq = await _context.Expenses.CountAsync() + 1;
        var expNum = await _settingsService.GenerateDocumentNumberAsync("Prefix_EXP", "EXP", seq);

        var expense = new Expense
        {
            ExpenseNumber = expNum,
            ExpenseDate = model.ExpenseDate,
            ExpenseCategoryId = model.ExpenseCategoryId,
            Description = model.Description.Trim(),
            Amount = model.Amount,
            PaymentMethod = model.PaymentMethod,
            ReferenceNumber = model.ReferenceNumber?.Trim(),
            Notes = model.Notes?.Trim(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "CreateExpense", "Expense", expense.Id.ToString(),
            null, $"Recorded Expense {expense.ExpenseNumber}: {expense.Amount:N2} ({expense.Description})", ipAddress);

        return expense;
    }

    public async Task<ExpenseCategory> CreateCategoryAsync(ExpenseCategoryViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var cat = new ExpenseCategory
        {
            Name = model.Name.Trim(),
            Description = model.Description?.Trim(),
            IsActive = model.IsActive
        };
        _context.ExpenseCategories.Add(cat);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "CreateExpenseCategory", "ExpenseCategory", cat.Id.ToString(),
            null, $"Created Expense Category: {cat.Name}", ipAddress);

        return cat;
    }
}

public interface IReportingService
{
    Task<DashboardViewModel> GetDashboardViewModelAsync();
}

public class ReportingService : IReportingService
{
    private readonly ApplicationDbContext _context;

    public ReportingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardViewModel> GetDashboardViewModelAsync()
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var todaySalesList = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .Where(s => s.SaleDate >= todayStart && s.SaleDate < todayEnd && s.Status != "Cancelled")
            .ToListAsync();

        decimal todaySales = todaySalesList.Sum(s => s.TotalAmount);
        decimal todayCogs = todaySalesList.SelectMany(s => s.Items).Sum(i => (i.Quantity - i.ReturnedQuantity) * i.UnitCost);
        decimal todayCashSales = todaySalesList.Sum(s => Math.Min(s.TotalAmount, s.PaidAmount));
        decimal todayCreditSales = todaySalesList.Sum(s => s.DueAmount);

        decimal todayPurchases = (await _context.GoodsReceipts
            .AsNoTracking()
            .Where(g => g.ReceivingDate >= todayStart && g.ReceivingDate < todayEnd && g.Status == "Completed")
            .Select(g => g.TotalAmount)
            .ToListAsync()).Sum();

        decimal todayReturns = (await _context.SalesReturns
            .AsNoTracking()
            .Where(sr => sr.ReturnDate >= todayStart && sr.ReturnDate < todayEnd && sr.Status == "Completed")
            .Select(sr => sr.TotalRefundAmount)
            .ToListAsync()).Sum();

        decimal todayExpenses = (await _context.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate >= todayStart && e.ExpenseDate < todayEnd)
            .Select(e => e.Amount)
            .ToListAsync()).Sum();

        decimal todayGrossProfit = (todaySales - todayReturns) - todayCogs;
        decimal todayNetProfit = todayGrossProfit - todayExpenses;

        // Inventory metrics
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .ToListAsync();

        int totalProducts = products.Count;
        int lowStockCount = products.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel);
        int outOfStockCount = products.Count(p => p.CurrentStock <= 0);
        decimal invCostValue = products.Sum(p => Math.Max(0, p.CurrentStock) * p.CostPrice);
        decimal invRetailValue = products.Sum(p => Math.Max(0, p.CurrentStock) * p.SalePrice);

        // Last 7 days chart
        var sevenDaysAgo = todayStart.AddDays(-6);
        var recentSales7d = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .Where(s => s.SaleDate >= sevenDaysAgo && s.Status != "Cancelled")
            .ToListAsync();

        var recentGrns7d = await _context.GoodsReceipts
            .AsNoTracking()
            .Where(g => g.ReceivingDate >= sevenDaysAgo && g.Status == "Completed")
            .ToListAsync();

        var recentExp7d = await _context.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate >= sevenDaysAgo)
            .ToListAsync();

        var chartPoints = new List<DailyChartPointDto>();
        for (int i = 0; i < 7; i++)
        {
            var d = sevenDaysAgo.AddDays(i);
            var nextD = d.AddDays(1);
            var dSales = recentSales7d.Where(s => s.SaleDate >= d && s.SaleDate < nextD).ToList();
            var sAmt = dSales.Sum(s => s.TotalAmount);
            var cogsAmt = dSales.SelectMany(s => s.Items).Sum(it => (it.Quantity - it.ReturnedQuantity) * it.UnitCost);
            var pAmt = recentGrns7d.Where(g => g.ReceivingDate >= d && g.ReceivingDate < nextD).Sum(g => g.TotalAmount);
            var eAmt = recentExp7d.Where(e => e.ExpenseDate >= d && e.ExpenseDate < nextD).Sum(e => e.Amount);

            chartPoints.Add(new DailyChartPointDto
            {
                DateLabel = d.ToString("MMM dd"),
                Sales = sAmt,
                Purchases = pAmt,
                Profit = (sAmt - cogsAmt - eAmt)
            });
        }

        // Payment Methods breakdown (all time / last 30 days)
        var allSales = await _context.Sales
            .AsNoTracking()
            .Where(s => s.Status != "Cancelled")
            .ToListAsync();

        var paymentMethodsChart = allSales
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new TopItemChartDto
            {
                Label = g.Key,
                Value = g.Sum(x => x.TotalAmount),
                Quantity = g.Count()
            })
            .OrderByDescending(x => x.Value)
            .ToList();

        // Top Products & Categories
        var allSaleItems = await _context.SaleItems
            .AsNoTracking()
            .Include(si => si.Product)
                .ThenInclude(p => p!.Category)
            .Include(si => si.Sale)
            .Where(si => si.Sale != null && si.Sale.Status != "Cancelled")
            .ToListAsync();

        var topProducts = allSaleItems
            .GroupBy(si => si.Product?.Name ?? $"Product #{si.ProductId}")
            .Select(g => new TopItemChartDto
            {
                Label = g.Key,
                Value = g.Sum(x => x.LineTotal),
                Quantity = g.Sum(x => x.Quantity - x.ReturnedQuantity)
            })
            .OrderByDescending(x => x.Value)
            .Take(6)
            .ToList();

        var topCategories = allSaleItems
            .GroupBy(si => si.Product?.Category?.Name ?? "Uncategorized")
            .Select(g => new TopItemChartDto
            {
                Label = g.Key,
                Value = g.Sum(x => x.LineTotal),
                Quantity = g.Sum(x => x.Quantity - x.ReturnedQuantity)
            })
            .OrderByDescending(x => x.Value)
            .Take(6)
            .ToList();

        var recentSalesList = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .OrderByDescending(s => s.SaleDate)
            .Take(8)
            .ToListAsync();

        var lowStockList = products
            .Where(p => p.CurrentStock <= p.MinimumStockLevel)
            .OrderBy(p => p.CurrentStock)
            .Take(8)
            .ToList();

        return new DashboardViewModel
        {
            TodaySales = todaySales,
            TodayPurchases = todayPurchases,
            TodayGrossProfit = todayGrossProfit,
            TodayExpenses = todayExpenses,
            TodayNetProfit = todayNetProfit,
            TodayCashSales = todayCashSales,
            TodayCreditSales = todayCreditSales,
            TodayReturns = todayReturns,
            TodayTransactions = todaySalesList.Count,

            TotalProducts = totalProducts,
            LowStockCount = lowStockCount,
            OutOfStockCount = outOfStockCount,
            InventoryCostValue = invCostValue,
            InventoryRetailValue = invRetailValue,

            Last7DaysChart = chartPoints,
            PaymentMethodsChart = paymentMethodsChart,
            TopProductsChart = topProducts,
            TopCategoriesChart = topCategories,
            RecentSales = recentSalesList,
            LowStockProducts = lowStockList
        };
    }
}
