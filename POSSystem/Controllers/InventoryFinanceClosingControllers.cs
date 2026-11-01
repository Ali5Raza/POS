using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using POSSystem.Authorization;
using POSSystem.Data;
using POSSystem.Helpers;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;
using POSSystem.Services;

namespace POSSystem.Controllers;

[Authorize]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventoryService;
    private readonly IProductService _productService;
    private readonly ApplicationDbContext _context;

    public InventoryController(
        IInventoryService inventoryService,
        IProductService productService,
        ApplicationDbContext context)
    {
        _inventoryService = inventoryService;
        _productService = productService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewInventory)]
    public async Task<IActionResult> Index(string? search, int? categoryId, string? stockFilter, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.StockFilter = stockFilter;
        ViewBag.Categories = new SelectList(await _context.ProductCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);

        var allActive = await _context.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
        ViewBag.TotalCostValuation = allActive.Sum(p => Math.Max(0, p.CurrentStock) * p.CostPrice);
        ViewBag.TotalRetailValuation = allActive.Sum(p => Math.Max(0, p.CurrentStock) * p.SalePrice);
        ViewBag.LowStockCount = allActive.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel);
        ViewBag.OutOfStockCount = allActive.Count(p => p.CurrentStock <= 0);

        var query = _productService.QueryProducts(search, categoryId, stockFilter);
        var paged = await PaginatedList<Product>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewInventory)]
    public async Task<IActionResult> Ledger(int? productId, string? transactionType, string? search, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.ProductId = productId;
        ViewBag.TransactionType = transactionType;
        ViewBag.Search = search;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.Products = new SelectList(await _context.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(), "Id", "Name", productId);

        var query = _inventoryService.QueryStockMovements(productId, transactionType, search, fromDate, toDate);
        var paged = await PaginatedList<StockMovement>.CreateAsync(query, page, 20);
        return View(paged);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ViewInventory)]
    public async Task<IActionResult> Adjustments(int? productId, string? search, int page = 1)
    {
        ViewBag.ProductId = productId;
        ViewBag.Search = search;
        ViewBag.Products = await _context.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();

        var query = _inventoryService.QueryAdjustments(productId, search);
        var paged = await PaginatedList<StockAdjustment>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.AdjustInventory)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(StockAdjustmentViewModel model)
    {
        try
        {
            var adj = await _inventoryService.AdjustStockAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Stock Adjustment {adj.AdjustmentNumber} completed ({adj.AdjustmentType} {adj.Quantity:N0} -> New Stock: {adj.NewQuantity:N0}).";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Adjustments));
    }
}

[Authorize]
public class PaymentsController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly ApplicationDbContext _context;

    public PaymentsController(IPaymentService paymentService, ApplicationDbContext context)
    {
        _paymentService = paymentService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewPayments)]
    public async Task<IActionResult> Index(string? search, string? paymentType, string? paymentMethod, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.PaymentType = paymentType;
        ViewBag.PaymentMethod = paymentMethod;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var query = _paymentService.QueryPayments(search, paymentType, paymentMethod, fromDate, toDate);
        var paged = await PaginatedList<Payment>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreatePayment)]
    public async Task<IActionResult> Create(string? paymentType = null, int? customerId = null, int? supplierId = null)
    {
        await PopulateDropdownsAsync(customerId, supplierId);
        return View(new PaymentViewModel
        {
            PaymentDate = DateTime.UtcNow.Date,
            PaymentType = paymentType ?? (supplierId.HasValue ? "SupplierPayment" : "CustomerReceipt"),
            CustomerId = customerId,
            SupplierId = supplierId,
            PaymentMethod = "Cash"
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreatePayment)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PaymentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.CustomerId, model.SupplierId);
            return View(model);
        }

        try
        {
            var p = await _paymentService.CreatePaymentAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Payment {p.PaymentNumber} ({p.Amount:N2}) recorded successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateDropdownsAsync(model.CustomerId, model.SupplierId);
            return View(model);
        }
    }

    private async Task PopulateDropdownsAsync(int? customerId = null, int? supplierId = null)
    {
        ViewBag.Customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsWalkIn)
            .OrderBy(c => c.Name)
            .ToListAsync();

        ViewBag.Suppliers = await _context.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.CompanyName)
            .ToListAsync();
    }
}

[Authorize]
public class ExpensesController : Controller
{
    private readonly IExpenseService _expenseService;

    public ExpensesController(IExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    [RequirePermission(PermissionCodes.ViewExpenses)]
    public async Task<IActionResult> Index(string? search, int? categoryId, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        var categories = await _expenseService.GetCategoriesAsync();
        ViewBag.Categories = new SelectList(categories, "Id", "Name", categoryId);
        ViewBag.CategoryList = categories;

        var query = _expenseService.QueryExpenses(search, categoryId, fromDate, toDate);
        ViewBag.TotalFilteredExpenses = (await query.Select(e => e.Amount).ToListAsync()).Sum();

        var paged = await PaginatedList<Expense>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateExpense)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExpenseViewModel model)
    {
        try
        {
            var exp = await _expenseService.CreateExpenseAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Expense {exp.ExpenseNumber} ({exp.Amount:N2}) recorded.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateExpense)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(ExpenseCategoryViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            TempData["ErrorMessage"] = "Category name is required.";
            return RedirectToAction(nameof(Index));
        }

        var cat = await _expenseService.CreateCategoryAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = $"Expense Category '{cat.Name}' added.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class DayClosingController : Controller
{
    private readonly IDayClosingService _dayClosingService;

    public DayClosingController(IDayClosingService dayClosingService)
    {
        _dayClosingService = dayClosingService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DayClosing)]
    public async Task<IActionResult> Index(DateTime? date = null)
    {
        var targetDate = date?.Date ?? DateTime.UtcNow.Date;
        var vm = await _dayClosingService.GetDayClosingViewModelAsync(targetDate);
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DayClosing)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(DayClosingViewModel model)
    {
        try
        {
            var userId = User.GetUserId() ?? 1;
            var closing = await _dayClosingService.CloseDayAsync(model, userId, User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Day Closing finalized for {closing.ClosingDate:yyyy-MM-dd}. Difference: {closing.Difference:N2}.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), new { date = model.ClosingDate.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DayClosing)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, DateTime date)
    {
        try
        {
            var userId = User.GetUserId() ?? 1;
            await _dayClosingService.ReopenDayAsync(id, userId, User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Day Closing for {date:yyyy-MM-dd} has been reopened.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }
}

[Authorize]
public class MonthlyClosingController : Controller
{
    private readonly IMonthlyClosingService _monthlyClosingService;

    public MonthlyClosingController(IMonthlyClosingService monthlyClosingService)
    {
        _monthlyClosingService = monthlyClosingService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.MonthlyClosing)]
    public async Task<IActionResult> Index(int? year = null, int? month = null)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var m = month ?? DateTime.UtcNow.Month;
        var vm = await _monthlyClosingService.GetMonthlyClosingViewModelAsync(y, m);
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.MonthlyClosing)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int year, int month, string? notes)
    {
        try
        {
            var userId = User.GetUserId() ?? 1;
            await _monthlyClosingService.CloseMonthAsync(year, month, notes, userId, User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Monthly Closing finalized for {year}-{month:D2}. Accounting period is now locked for normal users.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.MonthlyClosing)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, int year, int month)
    {
        try
        {
            var userId = User.GetUserId() ?? 1;
            await _monthlyClosingService.ReopenMonthAsync(id, userId, User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Monthly Closing for {year}-{month:D2} has been reopened.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), new { year, month });
    }
}
