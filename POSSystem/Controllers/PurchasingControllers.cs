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
public class PurchaseOrdersController : Controller
{
    private readonly IPurchaseService _purchaseService;
    private readonly ApplicationDbContext _context;

    public PurchaseOrdersController(IPurchaseService purchaseService, ApplicationDbContext context)
    {
        _purchaseService = purchaseService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Index(string? search, string? status, int? supplierId, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.SupplierId = supplierId;
        ViewBag.Suppliers = new SelectList(await _context.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync(), "Id", "CompanyName", supplierId);

        var query = _purchaseService.QueryPurchaseOrders(search, status, supplierId);
        var paged = await PaginatedList<PurchaseOrder>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Details(int id)
    {
        var po = await _purchaseService.GetByIdAsync(id);
        if (po == null) return NotFound();
        return View(po);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    public async Task<IActionResult> Create()
    {
        await PopulateViewDataAsync();
        var vm = new PurchaseCreateViewModel
        {
            OrderDate = DateTime.UtcNow.Date,
            ExpectedDeliveryDate = DateTime.UtcNow.Date.AddDays(3),
            Status = "Ordered",
            Items = new List<PurchaseItemViewModel> { new() { Quantity = 10 } }
        };
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CreatePurchase)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseCreateViewModel model)
    {
        try
        {
            var po = await _purchaseService.CreatePurchaseOrderAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order {po.PONumber} created successfully.";
            return RedirectToAction(nameof(Details), new { id = po.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewDataAsync(model.SupplierId);
            if (!model.Items.Any()) model.Items.Add(new PurchaseItemViewModel { Quantity = 1 });
            return View(model);
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ApprovePurchase)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        try
        {
            await _purchaseService.UpdateStatusAsync(id, status, User.GetUserId(), User.GetUsername(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["SuccessMessage"] = $"Purchase Order status updated to {status}.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateViewDataAsync(int? supplierId = null)
    {
        ViewBag.Suppliers = new SelectList(
            await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.CompanyName).ToListAsync(),
            "Id", "CompanyName", supplierId);
        ViewBag.Products = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}

[Authorize]
public class PurchasesController : Controller
{
    private readonly IGoodsReceivingService _grnService;
    private readonly ApplicationDbContext _context;

    public PurchasesController(IGoodsReceivingService grnService, ApplicationDbContext context)
    {
        _grnService = grnService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Index(string? search, int? supplierId, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.SupplierId = supplierId;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.Suppliers = new SelectList(await _context.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync(), "Id", "CompanyName", supplierId);

        var query = _grnService.QueryGoodsReceipts(search, supplierId, fromDate, toDate);
        var paged = await PaginatedList<GoodsReceipt>.CreateAsync(query, page, 15);
        return View(paged);
    }
}

[Authorize]
public class GoodsReceivingController : Controller
{
    private readonly IGoodsReceivingService _grnService;
    private readonly IPurchaseService _purchaseService;
    private readonly ApplicationDbContext _context;

    public GoodsReceivingController(
        IGoodsReceivingService grnService,
        IPurchaseService purchaseService,
        ApplicationDbContext context)
    {
        _grnService = grnService;
        _purchaseService = purchaseService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Index(string? search, int? supplierId, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.SupplierId = supplierId;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.Suppliers = new SelectList(await _context.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync(), "Id", "CompanyName", supplierId);

        var query = _grnService.QueryGoodsReceipts(search, supplierId, fromDate, toDate);
        var paged = await PaginatedList<GoodsReceipt>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Details(int id)
    {
        var grn = await _grnService.GetByIdAsync(id);
        if (grn == null) return NotFound();
        return View(grn);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ReceiveGoods)]
    public async Task<IActionResult> Create(int? purchaseOrderId = null)
    {
        var vm = new GoodsReceiptViewModel
        {
            ReceivingDate = DateTime.UtcNow.Date,
            PaymentMethod = "Cash"
        };

        if (purchaseOrderId.HasValue && purchaseOrderId.Value > 0)
        {
            var po = await _purchaseService.GetByIdAsync(purchaseOrderId.Value);
            if (po != null)
            {
                vm.PurchaseOrderId = po.Id;
                vm.PONumber = po.PONumber;
                vm.SupplierId = po.SupplierId;
                vm.DiscountAmount = po.DiscountAmount;
                vm.TaxAmount = po.TaxAmount;
                vm.Notes = $"Received against {po.PONumber}";
                vm.Items = po.Items.Select(i => new GoodsReceiptItemViewModel
                {
                    PurchaseOrderItemId = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name,
                    SKU = i.Product?.SKU,
                    OrderedQuantity = i.OrderedQuantity,
                    PreviouslyReceivedQuantity = i.ReceivedQuantity,
                    ReceivedQuantity = Math.Max(0, i.OrderedQuantity - i.ReceivedQuantity),
                    DamagedQuantity = 0,
                    CostPrice = i.CostPrice,
                    DiscountAmount = i.DiscountAmount,
                    TaxAmount = i.TaxAmount
                }).ToList();
            }
        }

        if (!vm.Items.Any())
        {
            vm.Items.Add(new GoodsReceiptItemViewModel { OrderedQuantity = 10, ReceivedQuantity = 10 });
        }

        await PopulateViewDataAsync(vm.SupplierId, vm.PurchaseOrderId);
        return View(vm);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ReceiveGoods)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoodsReceiptViewModel model)
    {
        try
        {
            var grn = await _grnService.CreateGoodsReceiptAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Goods Receipt {grn.GRNNumber} completed. Inventory stock increased automatically.";
            return RedirectToAction(nameof(Details), new { id = grn.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewDataAsync(model.SupplierId, model.PurchaseOrderId);
            if (!model.Items.Any()) model.Items.Add(new GoodsReceiptItemViewModel { ReceivedQuantity = 1 });
            return View(model);
        }
    }

    private async Task PopulateViewDataAsync(int? supplierId = null, int? poId = null)
    {
        ViewBag.Suppliers = new SelectList(
            await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.CompanyName).ToListAsync(),
            "Id", "CompanyName", supplierId);

        ViewBag.OpenPOs = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.Status == "Ordered" || p.Status == "PartiallyReceived" || p.Status == "Draft")
            .OrderByDescending(p => p.OrderDate)
            .ToListAsync();

        ViewBag.Products = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}

[Authorize]
public class PurchaseReturnsController : Controller
{
    private readonly IPurchaseReturnService _returnService;
    private readonly ApplicationDbContext _context;

    public PurchaseReturnsController(IPurchaseReturnService returnService, ApplicationDbContext context)
    {
        _returnService = returnService;
        _context = context;
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Index(string? search, int? supplierId, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.SupplierId = supplierId;
        ViewBag.Suppliers = new SelectList(await _context.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync(), "Id", "CompanyName", supplierId);

        var query = _returnService.QueryPurchaseReturns(search, supplierId);
        var paged = await PaginatedList<PurchaseReturn>.CreateAsync(query, page, 15);
        return View(paged);
    }

    [RequirePermission(PermissionCodes.ViewPurchases)]
    public async Task<IActionResult> Details(int id)
    {
        var pr = await _returnService.GetByIdAsync(id);
        if (pr == null) return NotFound();
        return View(pr);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PurchaseReturn)]
    public async Task<IActionResult> Create(int? goodsReceiptId = null)
    {
        ViewBag.GoodsReceipts = await _context.GoodsReceipts
            .AsNoTracking()
            .Include(g => g.Supplier)
            .Where(g => g.Status == "Completed")
            .OrderByDescending(g => g.ReceivingDate)
            .Take(50)
            .ToListAsync();

        if (!goodsReceiptId.HasValue || goodsReceiptId.Value <= 0)
        {
            var firstGrn = await _context.GoodsReceipts.AsNoTracking().OrderByDescending(g => g.Id).FirstOrDefaultAsync();
            if (firstGrn != null) goodsReceiptId = firstGrn.Id;
        }

        if (goodsReceiptId.HasValue && goodsReceiptId.Value > 0)
        {
            var vm = await _returnService.BuildReturnViewModelAsync(goodsReceiptId.Value);
            return View(vm);
        }

        return View(new PurchaseReturnViewModel());
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.PurchaseReturn)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseReturnViewModel model)
    {
        try
        {
            var pr = await _returnService.CreatePurchaseReturnAsync(
                model,
                User.GetUserId(),
                User.GetUsername(),
                User.IsInRole("Admin"),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Return {pr.ReturnNumber} completed. Stock and Supplier Balance adjusted.";
            return RedirectToAction(nameof(Details), new { id = pr.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.GoodsReceipts = await _context.GoodsReceipts
                .AsNoTracking()
                .Include(g => g.Supplier)
                .Where(g => g.Status == "Completed")
                .OrderByDescending(g => g.ReceivingDate)
                .Take(50)
                .ToListAsync();
            return View(model);
        }
    }
}
