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
public class DashboardController : Controller
{
    private readonly IReportingService _reportingService;

    public DashboardController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await _reportingService.GetDashboardViewModelAsync();
        return View(vm);
    }
}

[Authorize]
public class ProductsController : Controller
{
    private readonly IProductService _productService;
    private readonly ApplicationDbContext _context;

    public ProductsController(IProductService productService, ApplicationDbContext context)
    {
        _productService = productService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewProducts)]
    public async Task<IActionResult> Index(string? search, int? categoryId, string? stockFilter, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.StockFilter = stockFilter;
        ViewBag.Categories = new SelectList(await _context.ProductCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);

        var query = _productService.QueryProducts(search, categoryId, stockFilter);
        var paged = await PaginatedList<Product>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewProducts)]
    public async Task<IActionResult> Details(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        if (product == null) return NotFound();

        ViewBag.RecentMovements = await _context.StockMovements
            .AsNoTracking()
            .Include(sm => sm.User)
            .Where(sm => sm.ProductId == id)
            .OrderByDescending(sm => sm.CreatedAt)
            .Take(25)
            .ToListAsync();

        return View(product);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateProduct)]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        var nextCount = await _context.Products.CountAsync() + 1001;
        return View(new ProductCreateViewModel
        {
            SKU = $"SKU-{nextCount}",
            Barcode = $"896100{nextCount:D6}",
            Unit = "pcs",
            TaxRate = 5m,
            MinimumStockLevel = 5m,
            ReorderLevel = 15m,
            IsActive = true
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateProduct)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.CategoryId, model.DefaultSupplierId);
            return View(model);
        }

        try
        {
            var prod = await _productService.CreateProductAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Product '{prod.Name}' ({prod.SKU}) created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateDropdownsAsync(model.CategoryId, model.DefaultSupplierId);
            return View(model);
        }
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.EditProduct)]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await _productService.GetByIdAsync(id);
        if (p == null) return NotFound();

        await PopulateDropdownsAsync(p.CategoryId, p.DefaultSupplierId);
        return View(new ProductEditViewModel
        {
            Id = p.Id,
            SKU = p.SKU,
            Barcode = p.Barcode,
            Name = p.Name,
            Description = p.Description,
            CategoryId = p.CategoryId,
            DefaultSupplierId = p.DefaultSupplierId,
            CostPrice = p.CostPrice,
            SalePrice = p.SalePrice,
            WholesalePrice = p.WholesalePrice,
            MinimumSalePrice = p.MinimumSalePrice,
            TaxRate = p.TaxRate,
            Unit = p.Unit,
            CurrentStock = p.CurrentStock,
            MinimumStockLevel = p.MinimumStockLevel,
            ReorderLevel = p.ReorderLevel,
            IsActive = p.IsActive
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EditProduct)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.CategoryId, model.DefaultSupplierId);
            return View(model);
        }

        try
        {
            var p = await _productService.UpdateProductAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Product '{p.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateDropdownsAsync(model.CategoryId, model.DefaultSupplierId);
            return View(model);
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DeleteProduct)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        await _productService.ToggleProductActiveAsync(id, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = "Product status updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdownsAsync(int? selectedCat = null, int? selectedSup = null)
    {
        var cats = await _context.ProductCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        var sups = await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.CompanyName).ToListAsync();
        ViewBag.Categories = new SelectList(cats, "Id", "Name", selectedCat);
        ViewBag.Suppliers = new SelectList(sups, "Id", "CompanyName", selectedSup);
    }
}

[Authorize]
public class CategoriesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public CategoriesController(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    [RequirePermission(PermissionCodes.ViewProducts)]
    public async Task<IActionResult> Index(string? search)
    {
        ViewBag.Search = search;
        var query = _context.ProductCategories
            .AsNoTracking()
            .Include(c => c.ParentCategory)
            .Include(c => c.Products)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || (c.Description != null && c.Description.ToLower().Contains(s)));
        }

        var list = await query.OrderBy(c => c.Name).ToListAsync();
        ViewBag.ParentCategories = new SelectList(list.Where(c => c.IsActive), "Id", "Name");
        return View(list);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateProduct)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please provide a valid Category Name.";
            return RedirectToAction(nameof(Index));
        }

        if (model.Id > 0)
        {
            var cat = await _context.ProductCategories.FindAsync(model.Id);
            if (cat == null) return NotFound();

            cat.Name = model.Name.Trim();
            cat.Description = model.Description?.Trim();
            cat.ParentCategoryId = model.ParentCategoryId != model.Id ? model.ParentCategoryId : null;
            cat.IsActive = model.IsActive;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "UpdateCategory", "ProductCategory", cat.Id.ToString(), null, $"Updated category {cat.Name}");
            TempData["SuccessMessage"] = $"Category '{cat.Name}' updated.";
        }
        else
        {
            var cat = new ProductCategory
            {
                Name = model.Name.Trim(),
                Description = model.Description?.Trim(),
                ParentCategoryId = model.ParentCategoryId,
                IsActive = model.IsActive
            };
            _context.ProductCategories.Add(cat);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User.GetUserId(), User.GetUsername(), "CreateCategory", "ProductCategory", cat.Id.ToString(), null, $"Created category {cat.Name}");
            TempData["SuccessMessage"] = $"Category '{cat.Name}' created.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EditProduct)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var cat = await _context.ProductCategories.FindAsync(id);
        if (cat != null)
        {
            cat.IsActive = !cat.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Category '{cat.Name}' {(cat.IsActive ? "activated" : "deactivated")}.";
        }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class SuppliersController : Controller
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        ViewBag.Search = search;
        var query = _supplierService.QuerySuppliers(search);
        var paged = await PaginatedList<Supplier>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Details(int id)
    {
        var vm = await _supplierService.GetSupplierDetailsWithLedgerAsync(id);
        return View(vm);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    public IActionResult Create() => View(new SupplierViewModel { IsActive = true });

    [HttpPost]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplierViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var sup = await _supplierService.CreateSupplierAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = $"Supplier '{sup.CompanyName}' added successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _supplierService.GetByIdAsync(id);
        if (s == null) return NotFound();

        return View(new SupplierViewModel
        {
            Id = s.Id,
            Name = s.Name,
            CompanyName = s.CompanyName,
            Phone = s.Phone,
            Email = s.Email,
            Address = s.Address,
            TaxNumber = s.TaxNumber,
            OpeningBalance = s.OpeningBalance,
            CurrentBalance = s.CurrentBalance,
            IsActive = s.IsActive,
            Notes = s.Notes
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SupplierViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var sup = await _supplierService.UpdateSupplierAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = $"Supplier '{sup.CompanyName}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class CustomersController : Controller
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        ViewBag.Search = search;
        var query = _customerService.QueryCustomers(search);
        var paged = await PaginatedList<Customer>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Details(int id)
    {
        var vm = await _customerService.GetCustomerDetailsWithLedgerAsync(id);
        return View(vm);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public IActionResult Create() => View(new CustomerViewModel { CreditLimit = 25000m, IsActive = true });

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var cust = await _customerService.CreateCustomerAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = $"Customer '{cust.Name}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await _customerService.GetByIdAsync(id);
        if (c == null) return NotFound();

        return View(new CustomerViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Phone = c.Phone,
            Email = c.Email,
            Address = c.Address,
            OpeningBalance = c.OpeningBalance,
            CreditLimit = c.CreditLimit,
            CurrentBalance = c.CurrentBalance,
            IsWalkIn = c.IsWalkIn,
            IsActive = c.IsActive
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var c = await _customerService.UpdateCustomerAsync(model, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["SuccessMessage"] = $"Customer '{c.Name}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }
}
