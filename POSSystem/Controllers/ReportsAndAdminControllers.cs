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
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReportsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewReports)]
    public async Task<IActionResult> Index(
        string reportType = "DailySales",
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? search = null,
        int? categoryId = null,
        int? supplierId = null,
        int? customerId = null,
        string? export = null)
    {
        var start = (startDate ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var end = (endDate ?? DateTime.UtcNow.Date).Date;
        var endExclusive = end.AddDays(1);

        var filter = new ReportFilterViewModel
        {
            ReportType = reportType,
            StartDate = start,
            EndDate = end,
            Search = search,
            CategoryId = categoryId,
            SupplierId = supplierId,
            CustomerId = customerId
        };

        ViewBag.Filter = filter;
        ViewBag.Categories = new SelectList(await _context.ProductCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);
        ViewBag.Suppliers = new SelectList(await _context.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync(), "Id", "CompanyName", supplierId);
        ViewBag.Customers = new SelectList(await _context.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name", customerId);

        // Fetch data for reports
        var sales = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Category)
            .Where(s => s.SaleDate >= start && s.SaleDate < endExclusive && s.Status != "Cancelled")
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync();

        var salesReturns = await _context.SalesReturns
            .AsNoTracking()
            .Include(sr => sr.Sale)
            .Include(sr => sr.Customer)
            .Include(sr => sr.Items)
            .Where(sr => sr.ReturnDate >= start && sr.ReturnDate < endExclusive && sr.Status == "Completed")
            .OrderByDescending(sr => sr.ReturnDate)
            .ToListAsync();

        var purchases = await _context.GoodsReceipts
            .AsNoTracking()
            .Include(g => g.Supplier)
            .Include(g => g.PurchaseOrder)
            .Include(g => g.Items)
            .Where(g => g.ReceivingDate >= start && g.ReceivingDate < endExclusive && g.Status == "Completed")
            .OrderByDescending(g => g.ReceivingDate)
            .ToListAsync();

        var purchaseReturns = await _context.PurchaseReturns
            .AsNoTracking()
            .Include(pr => pr.GoodsReceipt)
            .Include(pr => pr.Supplier)
            .Where(pr => pr.ReturnDate >= start && pr.ReturnDate < endExclusive && pr.Status == "Completed")
            .OrderByDescending(pr => pr.ReturnDate)
            .ToListAsync();

        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var movements = await _context.StockMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.User)
            .Where(m => m.CreatedAt >= start && m.CreatedAt < endExclusive)
            .OrderByDescending(m => m.CreatedAt)
            .Take(250)
            .ToListAsync();

        var adjustments = await _context.StockAdjustments
            .AsNoTracking()
            .Include(a => a.Product)
            .Include(a => a.User)
            .Where(a => a.CreatedAt >= start && a.CreatedAt < endExclusive)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var expenses = await _context.Expenses
            .AsNoTracking()
            .Include(e => e.ExpenseCategory)
            .Include(e => e.User)
            .Where(e => e.ExpenseDate >= start && e.ExpenseDate < endExclusive)
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync();

        var payments = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Include(p => p.User)
            .Where(p => p.PaymentDate >= start && p.PaymentDate < endExclusive)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        var customers = (await _context.Customers.AsNoTracking().Where(c => !c.IsWalkIn).ToListAsync())
            .OrderByDescending(c => c.CurrentBalance)
            .ToList();
        var suppliers = (await _context.Suppliers.AsNoTracking().ToListAsync())
            .OrderByDescending(s => s.CurrentBalance)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            sales = sales.Where(x => x.InvoiceNumber.ToLower().Contains(s) || (x.Customer != null && x.Customer.Name.ToLower().Contains(s))).ToList();
            purchases = purchases.Where(x => x.GRNNumber.ToLower().Contains(s) || (x.Supplier != null && x.Supplier.CompanyName.ToLower().Contains(s))).ToList();
            products = products.Where(x => x.Name.ToLower().Contains(s) || x.SKU.ToLower().Contains(s) || x.Barcode.ToLower().Contains(s)).ToList();
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            products = products.Where(p => p.CategoryId == categoryId.Value).ToList();
        }

        if (supplierId.HasValue && supplierId.Value > 0)
        {
            purchases = purchases.Where(p => p.SupplierId == supplierId.Value).ToList();
        }

        if (customerId.HasValue && customerId.Value > 0)
        {
            sales = sales.Where(s => s.CustomerId == customerId.Value).ToList();
        }

        // Export CSV if requested
        if (string.Equals(export, "csv", StringComparison.OrdinalIgnoreCase))
        {
            if (!User.HasPermission(PermissionCodes.ExportReports))
                return Forbid();

            return ExportReportToCsv(reportType, sales, purchases, products, expenses, payments, customers, suppliers, salesReturns, purchaseReturns);
        }

        ViewBag.Sales = sales;
        ViewBag.SalesReturns = salesReturns;
        ViewBag.Purchases = purchases;
        ViewBag.PurchaseReturns = purchaseReturns;
        ViewBag.Products = products;
        ViewBag.Movements = movements;
        ViewBag.Adjustments = adjustments;
        ViewBag.Expenses = expenses;
        ViewBag.Payments = payments;
        ViewBag.CustomerList = customers;
        ViewBag.SupplierList = suppliers;

        return View();
    }

    private IActionResult ExportReportToCsv(
        string reportType,
        List<Sale> sales,
        List<GoodsReceipt> purchases,
        List<Product> products,
        List<Expense> expenses,
        List<Payment> payments,
        List<Customer> customers,
        List<Supplier> suppliers,
        List<SalesReturn> salesReturns,
        List<PurchaseReturn> purchaseReturns)
    {
        byte[] bytes;
        string fileName = $"{reportType}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        switch (reportType)
        {
            case "PurchaseReport":
            case "SupplierPurchaseReport":
            case "PurchaseSummary":
                bytes = CsvExportHelper.BuildCsv(purchases,
                    new[] { "GRN Number", "Date", "Supplier", "Invoice #", "SubTotal", "Discount", "Tax", "Total", "Paid", "Due" },
                    g => new object?[] { g.GRNNumber, g.ReceivingDate.ToString("yyyy-MM-dd"), g.Supplier?.CompanyName, g.SupplierInvoiceNumber, g.SubTotal, g.DiscountAmount, g.TaxAmount, g.TotalAmount, g.PaidAmount, g.DueAmount });
                break;

            case "CurrentStock":
            case "StockValuation":
            case "LowStock":
            case "OutOfStock":
            case "ProductProfitability":
                bytes = CsvExportHelper.BuildCsv(products,
                    new[] { "SKU", "Barcode", "Product Name", "Category", "Current Stock", "Min Stock", "Cost Price", "Sale Price", "Stock Cost Value", "Retail Value" },
                    p => new object?[] { p.SKU, p.Barcode, p.Name, p.Category?.Name, p.CurrentStock, p.MinimumStockLevel, p.CostPrice, p.SalePrice, p.CurrentStock * p.CostPrice, p.CurrentStock * p.SalePrice });
                break;

            case "Expenses":
                bytes = CsvExportHelper.BuildCsv(expenses,
                    new[] { "Expense #", "Date", "Category", "Description", "Payment Method", "Amount" },
                    e => new object?[] { e.ExpenseNumber, e.ExpenseDate.ToString("yyyy-MM-dd"), e.ExpenseCategory?.Name, e.Description, e.PaymentMethod, e.Amount });
                break;

            case "CustomerReceivables":
                bytes = CsvExportHelper.BuildCsv(customers,
                    new[] { "Customer Name", "Phone", "Email", "Credit Limit", "Current Receivable Balance" },
                    c => new object?[] { c.Name, c.Phone, c.Email, c.CreditLimit, c.CurrentBalance });
                break;

            case "SupplierPayables":
                bytes = CsvExportHelper.BuildCsv(suppliers,
                    new[] { "Company Name", "Contact Person", "Phone", "Tax/NTN", "Current Payable Balance" },
                    s => new object?[] { s.CompanyName, s.Name, s.Phone, s.TaxNumber, s.CurrentBalance });
                break;

            case "Payments":
            case "CashFlow":
                bytes = CsvExportHelper.BuildCsv(payments,
                    new[] { "Payment #", "Date", "Type", "Party", "Method", "Reference", "Amount" },
                    p => new object?[] { p.PaymentNumber, p.PaymentDate.ToString("yyyy-MM-dd HH:mm"), p.PaymentType, p.Customer?.Name ?? p.Supplier?.CompanyName ?? "-", p.PaymentMethod, p.ReferenceNumber, p.Amount });
                break;

            default:
                bytes = CsvExportHelper.BuildCsv(sales,
                    new[] { "Invoice #", "Date", "Customer", "Cashier", "Method", "SubTotal", "Discount", "Tax", "Total", "Paid", "Due", "Status" },
                    s => new object?[] { s.InvoiceNumber, s.SaleDate.ToString("yyyy-MM-dd HH:mm"), s.Customer?.Name, s.Cashier?.FullName, s.PaymentMethod, s.SubTotal, s.DiscountAmount, s.TaxAmount, s.TotalAmount, s.PaidAmount, s.DueAmount, s.Status });
                break;
        }

        return File(bytes, "text/csv", fileName);
    }
}

[Authorize]
public class UsersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public UsersController(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    [RequirePermission(PermissionCodes.ManageUsers)]
    public async Task<IActionResult> Index()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderBy(u => u.Username)
            .ToListAsync();
        return View(users);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ManageUsers)]
    public async Task<IActionResult> Create()
    {
        ViewBag.Roles = new SelectList(await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(), "Id", "Name");
        return View(new UserCreateViewModel { IsActive = true });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageUsers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower()))
        {
            ModelState.AddModelError("Username", "Username is already taken.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = new SelectList(await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(), "Id", "Name", model.RoleId);
            return View(model);
        }

        var user = new ApplicationUser
        {
            Username = model.Username.Trim(),
            FullName = model.FullName.Trim(),
            Email = model.Email.Trim(),
            RoleId = model.RoleId,
            IsActive = model.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = PasswordHelper.HashPassword(user, model.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "CreateUser", "ApplicationUser", user.Id.ToString(),
            null, $"Created user '{user.Username}' (RoleId={user.RoleId})", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["SuccessMessage"] = $"User account '{user.Username}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ManageUsers)]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        ViewBag.Roles = new SelectList(await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(), "Id", "Name", user.RoleId);
        return View(new UserEditViewModel
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            RoleId = user.RoleId,
            IsActive = user.IsActive
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageUsers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Roles = new SelectList(await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(), "Id", "Name", model.RoleId);
            return View(model);
        }

        var user = await _context.Users.FindAsync(model.Id);
        if (user == null) return NotFound();

        user.FullName = model.FullName.Trim();
        user.Email = model.Email.Trim();
        user.RoleId = model.RoleId;
        user.IsActive = model.IsActive;

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            user.PasswordHash = PasswordHelper.HashPassword(user, model.NewPassword);
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "UpdateUser", "ApplicationUser", user.Id.ToString(),
            null, $"Updated user '{user.Username}' (RoleId={user.RoleId}, Active={user.IsActive})", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["SuccessMessage"] = $"User '{user.Username}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageUsers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null && user.Username != "admin")
        {
            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "ToggleUserStatus", "ApplicationUser", user.Id.ToString(),
                null, $"Set user '{user.Username}' IsActive={user.IsActive}");
            TempData["SuccessMessage"] = $"User '{user.Username}' {(user.IsActive ? "activated" : "deactivated")}.";
        }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class RolesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public RolesController(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    [RequirePermission(PermissionCodes.ManageRoles)]
    public async Task<IActionResult> Index()
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .Include(r => r.Users)
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Id)
            .ToListAsync();
        return View(roles);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ManageRoles)]
    public async Task<IActionResult> Edit(int id)
    {
        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (role == null) return NotFound();

        var allPerms = await _context.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Name).ToListAsync();
        return View(new RoleEditViewModel
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            SelectedPermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToList(),
            AllPermissions = allPerms
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageRoles)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RoleEditViewModel model)
    {
        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == model.Id);

        if (role == null) return NotFound();

        if (!role.IsSystemRole && !string.IsNullOrWhiteSpace(model.Name))
            role.Name = model.Name.Trim();

        role.Description = model.Description?.Trim();

        _context.RolePermissions.RemoveRange(role.RolePermissions);
        var selectedIds = model.SelectedPermissionIds?.Distinct().ToList() ?? new List<int>();
        foreach (var pid in selectedIds)
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = pid
            });
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "UpdateRolePermissions", "Role", role.Id.ToString(),
            null, $"Updated permissions for role '{role.Name}' ({selectedIds.Count} permissions granted)", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["SuccessMessage"] = $"Permissions for role '{role.Name}' saved successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageRoles)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Role name is required.";
            return RedirectToAction(nameof(Index));
        }

        if (await _context.Roles.AnyAsync(r => r.Name.ToLower() == name.Trim().ToLower()))
        {
            TempData["ErrorMessage"] = "Role with this name already exists.";
            return RedirectToAction(nameof(Index));
        }

        var role = new Role
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            IsSystemRole = false
        };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "CreateRole", "Role", role.Id.ToString(), null, $"Created role '{role.Name}'");
        TempData["SuccessMessage"] = $"Role '{role.Name}' created. Configure its permissions below.";
        return RedirectToAction(nameof(Edit), new { id = role.Id });
    }
}

[Authorize]
public class PermissionsController : Controller
{
    private readonly ApplicationDbContext _context;

    public PermissionsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [RequirePermission(PermissionCodes.ManageRoles)]
    public async Task<IActionResult> Index()
    {
        var permissions = await _context.Permissions
            .AsNoTracking()
            .Include(p => p.RolePermissions)
                .ThenInclude(rp => rp.Role)
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .ToListAsync();

        ViewBag.Roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        return View(permissions);
    }
}

[Authorize]
public class SettingsController : Controller
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ManageSettings)]
    public async Task<IActionResult> Index()
    {
        var vm = await _settingsService.GetSettingsViewModelAsync();
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ManageSettings)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await _settingsService.SaveSettingsAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = "System settings updated and saved.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class AuditLogsController : Controller
{
    private readonly IAuditService _auditService;
    private readonly ApplicationDbContext _context;

    public AuditLogsController(IAuditService auditService, ApplicationDbContext context)
    {
        _auditService = auditService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewAuditLogs)]
    public async Task<IActionResult> Index(string? search, string? actionFilter, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.ActionFilter = actionFilter;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.DistinctActions = await _context.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

        var query = _auditService.QueryLogs(search, actionFilter, fromDate, toDate);
        var paged = await PaginatedList<AuditLog>.CreateAsync(query, page, 25);
        return View(paged);
    }
}
