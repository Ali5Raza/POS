using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface IDayClosingService
{
    Task<bool> IsDateLockedForUserAsync(DateTime date, bool isAdmin);
    Task EnsureDateOpenForTransactionAsync(DateTime date, bool isAdmin = false);
    Task<DayClosingViewModel> GetDayClosingViewModelAsync(DateTime date);
    Task<DayClosing> CloseDayAsync(DayClosingViewModel model, int userId, string username, string? ipAddress = null);
    Task ReopenDayAsync(int id, int userId, string username, string? ipAddress = null);
}

public class DayClosingService : IDayClosingService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public DayClosingService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<bool> IsDateLockedForUserAsync(DateTime date, bool isAdmin)
    {
        if (isAdmin) return false;

        var day = date.Date;
        var dayClosed = await _context.DayClosings
            .AsNoTracking()
            .AnyAsync(dc => dc.ClosingDate == day && dc.Status == "Closed");

        if (dayClosed) return true;

        var monthClosed = await _context.MonthlyClosings
            .AsNoTracking()
            .AnyAsync(mc => mc.Year == day.Year && mc.Month == day.Month && mc.Status == "Closed");

        return monthClosed;
    }

    public async Task EnsureDateOpenForTransactionAsync(DateTime date, bool isAdmin = false)
    {
        var day = date.Date;
        var dayClosed = await _context.DayClosings
            .AsNoTracking()
            .AnyAsync(dc => dc.ClosingDate == day && dc.Status == "Closed");

        if (dayClosed && !isAdmin)
        {
            throw new InvalidOperationException($"Day Closing has been finalized for {day:yyyy-MM-dd}. Normal users cannot add or modify transactions for a closed day.");
        }

        var monthClosed = await _context.MonthlyClosings
            .AsNoTracking()
            .AnyAsync(mc => mc.Year == day.Year && mc.Month == day.Month && mc.Status == "Closed");

        if (monthClosed && !isAdmin)
        {
            throw new InvalidOperationException($"Monthly Closing has been finalized for {day:yyyy-MM}. Normal users cannot add or modify transactions in a closed accounting period.");
        }
    }

    public async Task<DayClosingViewModel> GetDayClosingViewModelAsync(DateTime date)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var existing = await _context.DayClosings
            .Include(d => d.ClosedByUser)
            .Include(d => d.ReopenedByUser)
            .FirstOrDefaultAsync(d => d.ClosingDate == dayStart);

        var prevClosing = await _context.DayClosings
            .AsNoTracking()
            .Where(d => d.ClosingDate < dayStart && d.Status == "Closed")
            .OrderByDescending(d => d.ClosingDate)
            .FirstOrDefaultAsync();

        var daySales = await _context.Sales
            .AsNoTracking()
            .Where(s => s.SaleDate >= dayStart && s.SaleDate < dayEnd && s.Status != "Cancelled")
            .ToListAsync();

        decimal cashSales = daySales
            .Where(s => s.PaymentMethod == "Cash")
            .Sum(s => Math.Min(s.TotalAmount, s.PaidAmount));

        decimal cardSales = daySales
            .Where(s => s.PaymentMethod == "Card")
            .Sum(s => Math.Min(s.TotalAmount, s.PaidAmount));

        decimal bankSales = daySales
            .Where(s => s.PaymentMethod == "Bank Transfer" || s.PaymentMethod == "BankTransfer")
            .Sum(s => Math.Min(s.TotalAmount, s.PaidAmount));

        decimal customerPayments = (await _context.Payments
            .AsNoTracking()
            .Where(p => p.PaymentDate >= dayStart && p.PaymentDate < dayEnd
                        && p.PaymentType == "CustomerReceipt"
                        && p.PaymentMethod == "Cash")
            .Select(p => p.Amount)
            .ToListAsync()).Sum();

        decimal expenses = (await _context.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate >= dayStart && e.ExpenseDate < dayEnd && e.PaymentMethod == "Cash")
            .Select(e => e.Amount)
            .ToListAsync()).Sum();

        decimal refunds = (await _context.SalesReturns
            .AsNoTracking()
            .Where(sr => sr.ReturnDate >= dayStart && sr.ReturnDate < dayEnd
                         && sr.Status == "Completed"
                         && sr.RefundMethod == "Cash")
            .Select(sr => sr.TotalRefundAmount)
            .ToListAsync()).Sum();

        decimal openingCash = existing?.OpeningCash ?? prevClosing?.ActualCash ?? 10000m;
        decimal expectedCash = openingCash + cashSales + customerPayments - expenses - refunds;
        decimal actualCash = existing?.ActualCash ?? expectedCash;

        var pastClosings = await _context.DayClosings
            .AsNoTracking()
            .Include(d => d.ClosedByUser)
            .OrderByDescending(d => d.ClosingDate)
            .Take(20)
            .ToListAsync();

        return new DayClosingViewModel
        {
            ClosingDate = dayStart,
            OpeningCash = openingCash,
            CashSales = cashSales,
            CardSales = cardSales,
            BankSales = bankSales,
            CustomerPayments = customerPayments,
            Expenses = expenses,
            Refunds = refunds,
            ExpectedCash = expectedCash,
            ActualCash = actualCash,
            TotalTransactions = daySales.Count,
            IsAlreadyClosed = existing != null && existing.Status == "Closed",
            ExistingRecord = existing,
            Notes = existing?.Notes,
            PastClosings = pastClosings
        };
    }

    public async Task<DayClosing> CloseDayAsync(DayClosingViewModel model, int userId, string username, string? ipAddress = null)
    {
        var dayStart = model.ClosingDate.Date;
        var summary = await GetDayClosingViewModelAsync(dayStart);

        var existing = await _context.DayClosings.FirstOrDefaultAsync(d => d.ClosingDate == dayStart);
        var expectedCash = model.OpeningCash + summary.CashSales + summary.CustomerPayments - summary.Expenses - summary.Refunds;
        var difference = model.ActualCash - expectedCash;

        if (existing != null)
        {
            existing.OpeningCash = model.OpeningCash;
            existing.CashSales = summary.CashSales;
            existing.CardSales = summary.CardSales;
            existing.BankSales = summary.BankSales;
            existing.CustomerPayments = summary.CustomerPayments;
            existing.Expenses = summary.Expenses;
            existing.Refunds = summary.Refunds;
            existing.ExpectedCash = expectedCash;
            existing.ActualCash = model.ActualCash;
            existing.Difference = difference;
            existing.TotalTransactions = summary.TotalTransactions;
            existing.Status = "Closed";
            existing.Notes = model.Notes;
            existing.ClosedByUserId = userId;
            existing.ClosedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new DayClosing
            {
                ClosingDate = dayStart,
                OpeningCash = model.OpeningCash,
                CashSales = summary.CashSales,
                CardSales = summary.CardSales,
                BankSales = summary.BankSales,
                CustomerPayments = summary.CustomerPayments,
                Expenses = summary.Expenses,
                Refunds = summary.Refunds,
                ExpectedCash = expectedCash,
                ActualCash = model.ActualCash,
                Difference = difference,
                TotalTransactions = summary.TotalTransactions,
                Status = "Closed",
                Notes = model.Notes,
                ClosedByUserId = userId,
                ClosedAt = DateTime.UtcNow
            };
            _context.DayClosings.Add(existing);
        }

        await _context.SaveChangesAsync();
        await _auditService.LogAsync(userId, username, "DayClosing", "DayClosing", existing.Id.ToString(),
            null, $"Closed Day {dayStart:yyyy-MM-dd}. ExpectedCash={expectedCash:N2}, ActualCash={model.ActualCash:N2}, Diff={difference:N2}", ipAddress);

        return existing;
    }

    public async Task ReopenDayAsync(int id, int userId, string username, string? ipAddress = null)
    {
        var closing = await _context.DayClosings.FindAsync(id)
            ?? throw new InvalidOperationException("Day closing record not found.");

        closing.Status = "Reopened";
        closing.ReopenedByUserId = userId;
        closing.ReopenedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync(userId, username, "ReopenDayClosing", "DayClosing", closing.Id.ToString(),
            "Closed", $"Reopened Day {closing.ClosingDate:yyyy-MM-dd}", ipAddress);
    }
}

public interface IMonthlyClosingService
{
    Task<MonthlyClosingViewModel> GetMonthlyClosingViewModelAsync(int year, int month);
    Task<MonthlyClosing> CloseMonthAsync(int year, int month, string? notes, int userId, string username, string? ipAddress = null);
    Task ReopenMonthAsync(int id, int userId, string username, string? ipAddress = null);
}

public class MonthlyClosingService : IMonthlyClosingService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public MonthlyClosingService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<MonthlyClosingViewModel> GetMonthlyClosingViewModelAsync(int year, int month)
    {
        if (month < 1 || month > 12) month = DateTime.UtcNow.Month;
        if (year < 2000 || year > 2100) year = DateTime.UtcNow.Year;

        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);

        var existing = await _context.MonthlyClosings
            .Include(m => m.ClosedByUser)
            .Include(m => m.ReopenedByUser)
            .FirstOrDefaultAsync(m => m.Year == year && m.Month == month);

        var sales = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .Where(s => s.SaleDate >= startDate && s.SaleDate < endDate && s.Status != "Cancelled")
            .ToListAsync();

        decimal totalSales = sales.Sum(s => s.TotalAmount);
        decimal totalCogs = sales.SelectMany(s => s.Items).Sum(i => (i.Quantity - i.ReturnedQuantity) * i.UnitCost);

        decimal totalPurchases = (await _context.GoodsReceipts
            .AsNoTracking()
            .Where(g => g.ReceivingDate >= startDate && g.ReceivingDate < endDate && g.Status == "Completed")
            .Select(g => g.TotalAmount)
            .ToListAsync()).Sum();

        decimal salesReturns = (await _context.SalesReturns
            .AsNoTracking()
            .Where(sr => sr.ReturnDate >= startDate && sr.ReturnDate < endDate && sr.Status == "Completed")
            .Select(sr => sr.TotalRefundAmount)
            .ToListAsync()).Sum();

        decimal purchaseReturns = (await _context.PurchaseReturns
            .AsNoTracking()
            .Where(pr => pr.ReturnDate >= startDate && pr.ReturnDate < endDate && pr.Status == "Completed")
            .Select(pr => pr.TotalReturnAmount)
            .ToListAsync()).Sum();

        decimal totalExpenses = (await _context.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate >= startDate && e.ExpenseDate < endDate)
            .Select(e => e.Amount)
            .ToListAsync()).Sum();

        decimal netSales = totalSales - salesReturns;
        decimal grossProfit = netSales - totalCogs;
        decimal netProfit = grossProfit - totalExpenses;

        decimal receivables = (await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.CurrentBalance)
            .ToListAsync()).Sum();

        decimal payables = (await _context.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.CurrentBalance)
            .ToListAsync()).Sum();

        var activeProducts = await _context.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
        decimal inventoryValue = activeProducts.Sum(p => p.CurrentStock * p.CostPrice);

        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.PaymentDate >= startDate && p.PaymentDate < endDate && p.PaymentMethod == "Cash")
            .ToListAsync();

        decimal cashIn = payments.Where(p => p.PaymentType == "SaleReceipt" || p.PaymentType == "CustomerReceipt" || p.PaymentType == "PurchaseRefund").Sum(p => p.Amount);
        decimal cashOut = payments.Where(p => p.PaymentType == "PurchasePayment" || p.PaymentType == "SupplierPayment" || p.PaymentType == "SalesRefund").Sum(p => p.Amount) + totalExpenses;
        decimal cashSummary = cashIn - cashOut;

        var pastClosings = await _context.MonthlyClosings
            .AsNoTracking()
            .Include(m => m.ClosedByUser)
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .Take(12)
            .ToListAsync();

        return new MonthlyClosingViewModel
        {
            Year = year,
            Month = month,
            TotalSales = totalSales,
            TotalPurchases = totalPurchases,
            SalesReturns = salesReturns,
            PurchaseReturns = purchaseReturns,
            TotalExpenses = totalExpenses,
            GrossProfit = grossProfit,
            NetProfit = netProfit,
            Receivables = receivables,
            Payables = payables,
            InventoryValue = inventoryValue,
            CashSummary = cashSummary,
            IsAlreadyClosed = existing != null && existing.Status == "Closed",
            ExistingRecord = existing,
            Notes = existing?.Notes,
            PastClosings = pastClosings
        };
    }

    public async Task<MonthlyClosing> CloseMonthAsync(int year, int month, string? notes, int userId, string username, string? ipAddress = null)
    {
        var summary = await GetMonthlyClosingViewModelAsync(year, month);
        var existing = await _context.MonthlyClosings.FirstOrDefaultAsync(m => m.Year == year && m.Month == month);

        if (existing != null)
        {
            existing.TotalSales = summary.TotalSales;
            existing.TotalPurchases = summary.TotalPurchases;
            existing.SalesReturns = summary.SalesReturns;
            existing.PurchaseReturns = summary.PurchaseReturns;
            existing.TotalExpenses = summary.TotalExpenses;
            existing.GrossProfit = summary.GrossProfit;
            existing.NetProfit = summary.NetProfit;
            existing.Receivables = summary.Receivables;
            existing.Payables = summary.Payables;
            existing.InventoryValue = summary.InventoryValue;
            existing.CashSummary = summary.CashSummary;
            existing.Status = "Closed";
            existing.Notes = notes;
            existing.ClosedByUserId = userId;
            existing.ClosingDate = DateTime.UtcNow;
        }
        else
        {
            existing = new MonthlyClosing
            {
                Year = year,
                Month = month,
                TotalSales = summary.TotalSales,
                TotalPurchases = summary.TotalPurchases,
                SalesReturns = summary.SalesReturns,
                PurchaseReturns = summary.PurchaseReturns,
                TotalExpenses = summary.TotalExpenses,
                GrossProfit = summary.GrossProfit,
                NetProfit = summary.NetProfit,
                Receivables = summary.Receivables,
                Payables = summary.Payables,
                InventoryValue = summary.InventoryValue,
                CashSummary = summary.CashSummary,
                Status = "Closed",
                Notes = notes,
                ClosedByUserId = userId,
                ClosingDate = DateTime.UtcNow
            };
            _context.MonthlyClosings.Add(existing);
        }

        await _context.SaveChangesAsync();
        await _auditService.LogAsync(userId, username, "MonthlyClosing", "MonthlyClosing", existing.Id.ToString(),
            null, $"Closed Month {year}-{month:D2}. NetProfit={summary.NetProfit:N2}, InventoryValue={summary.InventoryValue:N2}", ipAddress);

        return existing;
    }

    public async Task ReopenMonthAsync(int id, int userId, string username, string? ipAddress = null)
    {
        var closing = await _context.MonthlyClosings.FindAsync(id)
            ?? throw new InvalidOperationException("Monthly closing record not found.");

        closing.Status = "Reopened";
        closing.ReopenedByUserId = userId;
        closing.ReopenedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync(userId, username, "ReopenMonthlyClosing", "MonthlyClosing", closing.Id.ToString(),
            "Closed", $"Reopened Month {closing.Year}-{closing.Month:D2}", ipAddress);
    }
}
