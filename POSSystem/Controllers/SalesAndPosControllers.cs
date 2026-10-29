using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POSSystem.Authorization;
using POSSystem.Data;
using POSSystem.Helpers;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;
using POSSystem.Services;

namespace POSSystem.Controllers;

[Authorize]
public class POSController : Controller
{
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly ISalesService _salesService;
    private readonly ISettingsService _settingsService;
    private readonly ApplicationDbContext _context;

    public POSController(
        IProductService productService,
        ICustomerService customerService,
        ISalesService salesService,
        ISettingsService settingsService,
        ApplicationDbContext context)
    {
        _productService = productService;
        _customerService = customerService;
        _salesService = salesService;
        _settingsService = settingsService;
        _context = context;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public async Task<IActionResult> Index()
    {
        var walkIn = await _customerService.GetOrCreateWalkInCustomerAsync();
        var categories = await _context.ProductCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var products = await _productService.SearchPosProductsAsync(null, null, 100);
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.IsWalkIn)
            .ThenBy(c => c.Name)
            .ToListAsync();

        var heldSales = await _salesService.GetHeldSalesAsync();
        var settings = await _settingsService.GetSettingsViewModelAsync();

        var vm = new POSViewModel
        {
            Categories = categories,
            Products = products,
            Customers = customers,
            HeldSales = heldSales,
            DefaultWalkInCustomer = walkIn,
            CanOverridePrice = User.HasPermission(PermissionCodes.OverridePrice),
            CanApplyHighDiscount = User.HasPermission(PermissionCodes.ApplyDiscount),
            AllowNegativeStock = settings.AllowNegativeStock,
            MaxNormalDiscountPercent = settings.MaximumDiscountPercent,
            EnableTax = settings.EnableTax,
            DefaultTaxRate = settings.DefaultTaxRate,
            DefaultPaymentMethod = settings.DefaultPaymentMethod
        };

        return View(vm);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public async Task<IActionResult> SearchProducts(string? term, int? categoryId)
    {
        var list = await _productService.SearchPosProductsAsync(term, categoryId, 80);
        return Json(list);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public async Task<IActionResult> ScanBarcode(string code)
    {
        var prod = await _productService.FindByBarcodeOrSkuAsync(code);
        if (prod == null)
        {
            return Json(new { found = false, message = $"No active product found matching barcode/SKU '{code}'." });
        }

        return Json(new
        {
            found = true,
            product = new
            {
                id = prod.Id,
                sku = prod.SKU,
                barcode = prod.Barcode,
                name = prod.Name,
                categoryId = prod.CategoryId,
                categoryName = prod.Category?.Name ?? "",
                costPrice = prod.CostPrice,
                salePrice = prod.SalePrice,
                wholesalePrice = prod.WholesalePrice > 0 ? prod.WholesalePrice : prod.SalePrice,
                minimumSalePrice = prod.MinimumSalePrice,
                taxRate = prod.TaxRate,
                unit = prod.Unit,
                currentStock = prod.CurrentStock
            }
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteSale([FromBody] SaleViewModel? jsonModel, SaleViewModel? formModel)
    {
        var model = (jsonModel != null && jsonModel.Items.Any()) ? jsonModel : (formModel ?? new SaleViewModel());
        bool isAjax = Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true
                      || Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        try
        {
            var sale = await _salesService.CompleteSaleAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.HasPermission(PermissionCodes.OverridePrice),
                User.HasPermission(PermissionCodes.ApplyDiscount),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    saleId = sale.Id,
                    invoiceNumber = sale.InvoiceNumber,
                    totalAmount = sale.TotalAmount,
                    paidAmount = sale.PaidAmount,
                    changeAmount = sale.ChangeAmount,
                    dueAmount = sale.DueAmount,
                    receiptUrl = Url.Action("Receipt", "Sales", new { id = sale.Id })
                });
            }

            TempData["SuccessMessage"] = $"Sale {sale.InvoiceNumber} completed! Change: {sale.ChangeAmount:N2}";
            return RedirectToAction("Receipt", "Sales", new { id = sale.Id });
        }
        catch (Exception ex)
        {
            if (isAjax)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }

            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HoldSale([FromBody] HoldSaleViewModel model)
    {
        var held = await _salesService.HoldSaleAsync(model, User.GetUserId());
        return Json(new
        {
            success = true,
            id = held.Id,
            holdReference = held.HoldReference,
            customerName = held.CustomerName,
            totalAmount = held.TotalAmount,
            createdAt = held.CreatedAt.ToString("HH:mm")
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreateSale)]
    public async Task<IActionResult> GetHeldSales()
    {
        var list = await _salesService.GetHeldSalesAsync();
        return Json(list);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreateSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteHeldSale(int id)
    {
        await _salesService.DeleteHeldSaleAsync(id);
        return Json(new { success = true });
    }
}

[Authorize]
public class SalesController : Controller
{
    private readonly ISalesService _salesService;

    public SalesController(ISalesService salesService)
    {
        _salesService = salesService;
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Index(string? search, string? status, string? paymentMethod, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.PaymentMethod = paymentMethod;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var query = _salesService.QuerySales(search, status, null, paymentMethod, fromDate, toDate);
        var paged = await PaginatedList<Sale>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Details(int id)
    {
        var sale = await _salesService.GetByIdAsync(id);
        if (sale == null) return NotFound();
        return View(sale);
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Receipt(int id, string? format = null, bool autoPrint = false)
    {
        var vm = await _salesService.BuildReceiptViewModelAsync(id, format, autoPrint);
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CancelSale)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        try
        {
            await _salesService.CancelSaleAsync(
                id,
                string.IsNullOrWhiteSpace(reason) ? "Voided by authorized user" : reason,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Sale invoice has been cancelled and inventory stock restored.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}

[Authorize]
public class SalesReturnsController : Controller
{
    private readonly ISalesReturnService _returnService;
    private readonly ISalesService _salesService;
    private readonly ApplicationDbContext _context;

    public SalesReturnsController(
        ISalesReturnService returnService,
        ISalesService salesService,
        ApplicationDbContext context)
    {
        _returnService = returnService;
        _salesService = salesService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Index(string? search, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var query = _returnService.QuerySalesReturns(search, fromDate, toDate);
        var paged = await PaginatedList<SalesReturn>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewSales)]
    public async Task<IActionResult> Details(int id)
    {
        var sr = await _returnService.GetByIdAsync(id);
        if (sr == null) return NotFound();
        return View(sr);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.SalesReturn)]
    public async Task<IActionResult> Create(int? saleId = null, string? invoiceNumber = null)
    {
        ViewBag.RecentSales = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.Status != "Cancelled")
            .OrderByDescending(s => s.SaleDate)
            .Take(50)
            .ToListAsync();

        if (!saleId.HasValue && !string.IsNullOrWhiteSpace(invoiceNumber))
        {
            var found = await _salesService.GetByInvoiceNumberAsync(invoiceNumber);
            if (found != null) saleId = found.Id;
            else TempData["ErrorMessage"] = $"Invoice '{invoiceNumber}' not found.";
        }

        if (!saleId.HasValue)
        {
            var latest = await _context.Sales.AsNoTracking().Where(s => s.Status != "Cancelled").OrderByDescending(s => s.Id).FirstOrDefaultAsync();
            if (latest != null) saleId = latest.Id;
        }

        if (saleId.HasValue && saleId.Value > 0)
        {
            var vm = await _returnService.BuildReturnViewModelAsync(saleId.Value);
            return View(vm);
        }

        return View(new SalesReturnViewModel());
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.SalesReturn)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SalesReturnViewModel model)
    {
        try
        {
            var sr = await _returnService.CreateSalesReturnAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Sales Return {sr.ReturnNumber} completed. Inventory stock increased by returned quantities.";
            return RedirectToAction(nameof(Details), new { id = sr.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.RecentSales = await _context.Sales
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.Status != "Cancelled")
                .OrderByDescending(s => s.SaleDate)
                .Take(50)
                .ToListAsync();
            return View(model);
        }
    }
}
