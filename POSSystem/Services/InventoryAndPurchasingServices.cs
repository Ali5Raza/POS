using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface IInventoryService
{
    IQueryable<StockMovement> QueryStockMovements(int? productId = null, string? transactionType = null, string? search = null, DateTime? fromDate = null, DateTime? toDate = null);
    IQueryable<StockAdjustment> QueryAdjustments(int? productId = null, string? search = null);
    Task<StockAdjustment> AdjustStockAsync(StockAdjustmentViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
}

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public InventoryService(
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

    public IQueryable<StockMovement> QueryStockMovements(int? productId = null, string? transactionType = null, string? search = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.StockMovements
            .AsNoTracking()
            .Include(sm => sm.Product)
            .Include(sm => sm.User)
            .Include(sm => sm.Warehouse)
            .AsQueryable();

        if (productId.HasValue && productId.Value > 0)
            query = query.Where(sm => sm.ProductId == productId.Value);

        if (!string.IsNullOrWhiteSpace(transactionType))
            query = query.Where(sm => sm.TransactionType == transactionType);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(sm =>
                sm.ReferenceNumber.ToLower().Contains(s) ||
                (sm.Product != null && (sm.Product.Name.ToLower().Contains(s) || sm.Product.SKU.ToLower().Contains(s))) ||
                (sm.Notes != null && sm.Notes.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(sm => sm.CreatedAt >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(sm => sm.CreatedAt < end);
        }

        return query.OrderByDescending(sm => sm.CreatedAt).ThenByDescending(sm => sm.Id);
    }

    public IQueryable<StockAdjustment> QueryAdjustments(int? productId = null, string? search = null)
    {
        var query = _context.StockAdjustments
            .AsNoTracking()
            .Include(sa => sa.Product)
            .Include(sa => sa.User)
            .AsQueryable();

        if (productId.HasValue && productId.Value > 0)
            query = query.Where(sa => sa.ProductId == productId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(sa =>
                sa.AdjustmentNumber.ToLower().Contains(s) ||
                sa.Reason.ToLower().Contains(s) ||
                (sa.Product != null && sa.Product.Name.ToLower().Contains(s)));
        }

        return query.OrderByDescending(sa => sa.CreatedAt);
    }

    public async Task<StockAdjustment> AdjustStockAsync(StockAdjustmentViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(DateTime.UtcNow, isAdmin);

        if (model.Quantity <= 0)
            throw new InvalidOperationException("Adjustment quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(model.Reason))
            throw new InvalidOperationException("Adjustment reason is required.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var product = await _context.Products.FindAsync(model.ProductId)
                ?? throw new InvalidOperationException("Product not found.");

            var allowNegative = await _settingsService.GetBoolAsync("AllowNegativeStock", false);
            bool isIncrease = string.Equals(model.AdjustmentType, "Increase", StringComparison.OrdinalIgnoreCase);

            if (!isIncrease && !allowNegative && product.CurrentStock < model.Quantity)
            {
                throw new InvalidOperationException($"Cannot decrease stock by {model.Quantity}. Current stock for '{product.Name}' is {product.CurrentStock}.");
            }

            var prevQty = product.CurrentStock;
            product.CurrentStock = isIncrease ? prevQty + model.Quantity : prevQty - model.Quantity;
            product.UpdatedAt = DateTime.UtcNow;

            var count = await _context.StockAdjustments.CountAsync() + 1;
            var adjNumber = $"ADJ-{DateTime.UtcNow:yyyyMM}-{count:D4}";
            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };

            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = adjNumber,
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                AdjustmentType = isIncrease ? "Increase" : "Decrease",
                Quantity = model.Quantity,
                PreviousQuantity = prevQty,
                NewQuantity = product.CurrentStock,
                UnitCost = product.CostPrice,
                Reason = model.Reason.Trim(),
                Notes = model.Notes?.Trim(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.StockAdjustments.Add(adjustment);

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                TransactionType = isIncrease ? "Adjustment In" : "Adjustment Out",
                ReferenceNumber = adjNumber,
                QuantityIn = isIncrease ? model.Quantity : 0m,
                QuantityOut = isIncrease ? 0m : model.Quantity,
                BalanceAfter = product.CurrentStock,
                UnitCost = product.CostPrice,
                Notes = $"{adjustment.AdjustmentType}: {adjustment.Reason}",
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "StockAdjustment", "Product", product.Id.ToString(),
                $"Stock={prevQty}", $"Stock={product.CurrentStock} ({adjustment.AdjustmentType} {model.Quantity}, Reason={adjustment.Reason})", ipAddress);

            await tx.CommitAsync();
            return adjustment;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}

public interface IPurchaseService
{
    IQueryable<PurchaseOrder> QueryPurchaseOrders(string? search = null, string? status = null, int? supplierId = null);
    Task<PurchaseOrder?> GetByIdAsync(int id);
    Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseCreateViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
    Task UpdateStatusAsync(int id, string newStatus, int? userId, string username, string? ipAddress = null);
}

public class PurchaseService : IPurchaseService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public PurchaseService(
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

    public IQueryable<PurchaseOrder> QueryPurchaseOrders(string? search = null, string? status = null, int? supplierId = null)
    {
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Include(po => po.Supplier)
            .Include(po => po.CreatedByUser)
            .Include(po => po.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(po => po.Status == status);

        if (supplierId.HasValue && supplierId.Value > 0)
            query = query.Where(po => po.SupplierId == supplierId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(po =>
                po.PONumber.ToLower().Contains(s) ||
                (po.Supplier != null && po.Supplier.CompanyName.ToLower().Contains(s)));
        }

        return query.OrderByDescending(po => po.OrderDate).ThenByDescending(po => po.Id);
    }

    public async Task<PurchaseOrder?> GetByIdAsync(int id)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.CreatedByUser)
            .Include(po => po.Items)
                .ThenInclude(i => i.Product)
            .Include(po => po.GoodsReceipts)
            .FirstOrDefaultAsync(po => po.Id == id);
    }

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseCreateViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(model.OrderDate, isAdmin);

        var validItems = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (!validItems.Any())
            throw new InvalidOperationException("Purchase Order must contain at least one item with a positive quantity.");

        var supplier = await _context.Suppliers.FindAsync(model.SupplierId)
            ?? throw new InvalidOperationException("Selected supplier does not exist.");

        var seq = await _context.PurchaseOrders.CountAsync() + 1;
        var poNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_PO", "PO", seq);

        decimal subTotal = 0m;
        decimal itemDiscounts = 0m;
        decimal itemTaxes = 0m;

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            SupplierId = supplier.Id,
            OrderDate = model.OrderDate,
            ExpectedDeliveryDate = model.ExpectedDeliveryDate,
            Status = string.IsNullOrWhiteSpace(model.Status) ? "Ordered" : model.Status,
            Notes = model.Notes?.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var itemVm in validItems)
        {
            var product = await _context.Products.FindAsync(itemVm.ProductId)
                ?? throw new InvalidOperationException($"Product ID {itemVm.ProductId} not found.");

            var cost = itemVm.CostPrice > 0 ? itemVm.CostPrice : product.CostPrice;
            var lineGross = itemVm.Quantity * cost;
            var lineTotal = lineGross - itemVm.DiscountAmount + itemVm.TaxAmount;

            subTotal += lineGross;
            itemDiscounts += itemVm.DiscountAmount;
            itemTaxes += itemVm.TaxAmount;

            po.Items.Add(new PurchaseOrderItem
            {
                ProductId = product.Id,
                OrderedQuantity = itemVm.Quantity,
                ReceivedQuantity = 0m,
                CostPrice = cost,
                DiscountAmount = itemVm.DiscountAmount,
                TaxAmount = itemVm.TaxAmount,
                LineTotal = lineTotal
            });
        }

        po.SubTotal = subTotal;
        po.DiscountAmount = model.DiscountAmount + itemDiscounts;
        po.TaxAmount = model.TaxAmount + itemTaxes;
        po.TotalAmount = Math.Max(0, po.SubTotal - po.DiscountAmount + po.TaxAmount);

        _context.PurchaseOrders.Add(po);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "CreatePurchaseOrder", "PurchaseOrder", po.Id.ToString(),
            null, $"Created {po.PONumber} for {supplier.CompanyName}, Total={po.TotalAmount:N2}", ipAddress);

        return po;
    }

    public async Task UpdateStatusAsync(int id, string newStatus, int? userId, string username, string? ipAddress = null)
    {
        var po = await _context.PurchaseOrders.Include(p => p.GoodsReceipts).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("Purchase Order not found.");

        if (newStatus == "Cancelled" && po.GoodsReceipts.Any(g => g.Status == "Completed"))
            throw new InvalidOperationException("Cannot cancel a Purchase Order that already has completed Goods Receipts.");

        var oldStatus = po.Status;
        po.Status = newStatus;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "UpdatePOStatus", "PurchaseOrder", po.Id.ToString(),
            oldStatus, $"{po.PONumber} status changed to {newStatus}", ipAddress);
    }
}

public interface IGoodsReceivingService
{
    IQueryable<GoodsReceipt> QueryGoodsReceipts(string? search = null, int? supplierId = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<GoodsReceipt?> GetByIdAsync(int id);
    Task<GoodsReceipt> CreateGoodsReceiptAsync(GoodsReceiptViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
}

public class GoodsReceivingService : IGoodsReceivingService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public GoodsReceivingService(
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

    public IQueryable<GoodsReceipt> QueryGoodsReceipts(string? search = null, int? supplierId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.GoodsReceipts
            .AsNoTracking()
            .Include(g => g.Supplier)
            .Include(g => g.PurchaseOrder)
            .Include(g => g.CreatedByUser)
            .Include(g => g.Items)
            .AsQueryable();

        if (supplierId.HasValue && supplierId.Value > 0)
            query = query.Where(g => g.SupplierId == supplierId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(g =>
                g.GRNNumber.ToLower().Contains(s) ||
                (g.SupplierInvoiceNumber != null && g.SupplierInvoiceNumber.ToLower().Contains(s)) ||
                (g.Supplier != null && g.Supplier.CompanyName.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(g => g.ReceivingDate >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(g => g.ReceivingDate < end);
        }

        return query.OrderByDescending(g => g.ReceivingDate).ThenByDescending(g => g.Id);
    }

    public async Task<GoodsReceipt?> GetByIdAsync(int id)
    {
        return await _context.GoodsReceipts
            .Include(g => g.Supplier)
            .Include(g => g.PurchaseOrder)
            .Include(g => g.CreatedByUser)
            .Include(g => g.Items)
                .ThenInclude(i => i.Product)
            .Include(g => g.PurchaseReturns)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<GoodsReceipt> CreateGoodsReceiptAsync(GoodsReceiptViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(model.ReceivingDate, isAdmin);

        var receivedItems = model.Items.Where(i => i.ProductId > 0 && i.ReceivedQuantity > 0).ToList();
        if (!receivedItems.Any())
            throw new InvalidOperationException("Goods Receipt must contain at least one item with a Received Quantity greater than zero.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var supplier = await _context.Suppliers.FindAsync(model.SupplierId)
                ?? throw new InvalidOperationException("Supplier not found.");

            PurchaseOrder? po = null;
            if (model.PurchaseOrderId.HasValue && model.PurchaseOrderId.Value > 0)
            {
                po = await _context.PurchaseOrders
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == model.PurchaseOrderId.Value);

                if (po != null && po.Status == "Cancelled")
                    throw new InvalidOperationException("Cannot receive goods against a cancelled Purchase Order.");
            }

            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };
            var seq = await _context.GoodsReceipts.CountAsync() + 1;
            var grnNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_GRN", "GRN", seq);

            decimal subTotal = 0m;
            decimal itemDiscounts = 0m;
            decimal itemTaxes = 0m;

            var grn = new GoodsReceipt
            {
                GRNNumber = grnNumber,
                SupplierId = supplier.Id,
                PurchaseOrderId = po?.Id,
                WarehouseId = warehouse.Id,
                ReceivingDate = model.ReceivingDate,
                SupplierInvoiceNumber = model.SupplierInvoiceNumber?.Trim(),
                PaymentMethod = string.IsNullOrWhiteSpace(model.PaymentMethod) ? "Cash" : model.PaymentMethod,
                Status = "Completed",
                Notes = model.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var itemVm in receivedItems)
            {
                var product = await _context.Products.FindAsync(itemVm.ProductId)
                    ?? throw new InvalidOperationException($"Product ID {itemVm.ProductId} not found.");

                var cost = itemVm.CostPrice > 0 ? itemVm.CostPrice : product.CostPrice;
                var lineGross = itemVm.ReceivedQuantity * cost;
                var lineTotal = lineGross - itemVm.DiscountAmount + itemVm.TaxAmount;

                subTotal += lineGross;
                itemDiscounts += itemVm.DiscountAmount;
                itemTaxes += itemVm.TaxAmount;

                grn.Items.Add(new GoodsReceiptItem
                {
                    PurchaseOrderItemId = itemVm.PurchaseOrderItemId,
                    ProductId = product.Id,
                    OrderedQuantity = itemVm.OrderedQuantity > 0 ? itemVm.OrderedQuantity : itemVm.ReceivedQuantity,
                    ReceivedQuantity = itemVm.ReceivedQuantity,
                    DamagedQuantity = itemVm.DamagedQuantity,
                    ReturnedQuantity = 0m,
                    CostPrice = cost,
                    DiscountAmount = itemVm.DiscountAmount,
                    TaxAmount = itemVm.TaxAmount,
                    LineTotal = lineTotal
                });

                // Increase Inventory Stock
                product.CurrentStock += itemVm.ReceivedQuantity;
                if (cost > 0) product.CostPrice = cost;
                product.UpdatedAt = DateTime.UtcNow;

                // Create Stock Ledger entry
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Purchase",
                    ReferenceNumber = grnNumber,
                    QuantityIn = itemVm.ReceivedQuantity,
                    QuantityOut = 0m,
                    BalanceAfter = product.CurrentStock,
                    UnitCost = cost,
                    Notes = $"Goods Received from {supplier.CompanyName}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });

                // Update PO Item if linked
                if (po != null)
                {
                    var poItem = po.Items.FirstOrDefault(pi =>
                        (itemVm.PurchaseOrderItemId.HasValue && pi.Id == itemVm.PurchaseOrderItemId.Value) ||
                        pi.ProductId == product.Id);

                    if (poItem != null)
                    {
                        poItem.ReceivedQuantity += itemVm.ReceivedQuantity;
                    }
                }
            }

            grn.SubTotal = subTotal;
            grn.DiscountAmount = model.DiscountAmount + itemDiscounts;
            grn.TaxAmount = model.TaxAmount + itemTaxes;
            grn.TotalAmount = Math.Max(0, grn.SubTotal - grn.DiscountAmount + grn.TaxAmount);
            grn.PaidAmount = Math.Max(0, model.PaidAmount);
            grn.DueAmount = Math.Max(0, grn.TotalAmount - grn.PaidAmount);

            _context.GoodsReceipts.Add(grn);
            await _context.SaveChangesAsync();

            // Update PO status if linked
            if (po != null)
            {
                bool allReceived = po.Items.All(i => i.ReceivedQuantity >= i.OrderedQuantity);
                bool anyReceived = po.Items.Any(i => i.ReceivedQuantity > 0);
                po.Status = allReceived ? "FullyReceived" : (anyReceived ? "PartiallyReceived" : po.Status);
            }

            // Update Supplier Payable Balance
            supplier.CurrentBalance += grn.DueAmount;

            // Create Payment record if PaidAmount > 0
            if (grn.PaidAmount > 0)
            {
                var paySeq = await _context.Payments.CountAsync() + 1;
                var payNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_PAY", "PAY", paySeq);
                _context.Payments.Add(new Payment
                {
                    PaymentNumber = payNumber,
                    PaymentDate = model.ReceivingDate,
                    PaymentType = "PurchasePayment",
                    Amount = grn.PaidAmount,
                    PaymentMethod = grn.PaymentMethod,
                    ReferenceNumber = grn.GRNNumber,
                    SupplierId = supplier.Id,
                    GoodsReceiptId = grn.Id,
                    Notes = $"Payment on Goods Receipt {grn.GRNNumber}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "GoodsReceiving", "GoodsReceipt", grn.Id.ToString(),
                null, $"Completed {grn.GRNNumber} from {supplier.CompanyName}. Total={grn.TotalAmount:N2}, Paid={grn.PaidAmount:N2}, Due={grn.DueAmount:N2}", ipAddress);

            await tx.CommitAsync();
            return grn;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}

public interface IPurchaseReturnService
{
    IQueryable<PurchaseReturn> QueryPurchaseReturns(string? search = null, int? supplierId = null);
    Task<PurchaseReturn?> GetByIdAsync(int id);
    Task<PurchaseReturnViewModel> BuildReturnViewModelAsync(int goodsReceiptId);
    Task<PurchaseReturn> CreatePurchaseReturnAsync(PurchaseReturnViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
}

public class PurchaseReturnService : IPurchaseReturnService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public PurchaseReturnService(
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

    public IQueryable<PurchaseReturn> QueryPurchaseReturns(string? search = null, int? supplierId = null)
    {
        var query = _context.PurchaseReturns
            .AsNoTracking()
            .Include(pr => pr.GoodsReceipt)
            .Include(pr => pr.Supplier)
            .Include(pr => pr.CreatedByUser)
            .Include(pr => pr.Items)
            .AsQueryable();

        if (supplierId.HasValue && supplierId.Value > 0)
            query = query.Where(pr => pr.SupplierId == supplierId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(pr =>
                pr.ReturnNumber.ToLower().Contains(s) ||
                (pr.GoodsReceipt != null && pr.GoodsReceipt.GRNNumber.ToLower().Contains(s)) ||
                (pr.Supplier != null && pr.Supplier.CompanyName.ToLower().Contains(s)));
        }

        return query.OrderByDescending(pr => pr.ReturnDate).ThenByDescending(pr => pr.Id);
    }

    public async Task<PurchaseReturn?> GetByIdAsync(int id)
    {
        return await _context.PurchaseReturns
            .Include(pr => pr.GoodsReceipt)
            .Include(pr => pr.Supplier)
            .Include(pr => pr.CreatedByUser)
            .Include(pr => pr.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(pr => pr.Id == id);
    }

    public async Task<PurchaseReturnViewModel> BuildReturnViewModelAsync(int goodsReceiptId)
    {
        var grn = await _context.GoodsReceipts
            .AsNoTracking()
            .Include(g => g.Supplier)
            .Include(g => g.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(g => g.Id == goodsReceiptId)
            ?? throw new InvalidOperationException("Goods Receipt Note not found.");

        return new PurchaseReturnViewModel
        {
            GoodsReceiptId = grn.Id,
            GRNNumber = grn.GRNNumber,
            SupplierId = grn.SupplierId,
            SupplierName = grn.Supplier?.CompanyName,
            ReturnDate = DateTime.UtcNow,
            Items = grn.Items.Select(i => new PurchaseReturnItemViewModel
            {
                GoodsReceiptItemId = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? "",
                SKU = i.Product?.SKU ?? "",
                ReceivedQuantity = i.ReceivedQuantity,
                AlreadyReturnedQuantity = i.ReturnedQuantity,
                UnitCost = i.CostPrice,
                ReturnQuantity = 0
            }).ToList()
        };
    }

    public async Task<PurchaseReturn> CreatePurchaseReturnAsync(PurchaseReturnViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(model.ReturnDate, isAdmin);

        var itemsToReturn = model.Items.Where(i => i.ReturnQuantity > 0).ToList();
        if (!itemsToReturn.Any())
            throw new InvalidOperationException("Please enter a return quantity greater than zero for at least one item.");

        if (string.IsNullOrWhiteSpace(model.Reason))
            throw new InvalidOperationException("Return reason is required.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var grn = await _context.GoodsReceipts
                .Include(g => g.Items)
                .FirstOrDefaultAsync(g => g.Id == model.GoodsReceiptId)
                ?? throw new InvalidOperationException("Original Goods Receipt not found.");

            var supplier = await _context.Suppliers.FindAsync(grn.SupplierId)
                ?? throw new InvalidOperationException("Supplier not found.");

            var allowNegative = await _settingsService.GetBoolAsync("AllowNegativeStock", false);
            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };

            var seq = await _context.PurchaseReturns.CountAsync() + 1;
            var returnNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_PR", "PR", seq);

            var purchaseReturn = new PurchaseReturn
            {
                ReturnNumber = returnNumber,
                GoodsReceiptId = grn.Id,
                SupplierId = supplier.Id,
                ReturnDate = model.ReturnDate,
                Reason = model.Reason.Trim(),
                Status = "Completed",
                Notes = model.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            decimal totalRefund = 0m;

            foreach (var itemVm in itemsToReturn)
            {
                var grnItem = grn.Items.FirstOrDefault(gi => gi.Id == itemVm.GoodsReceiptItemId || gi.ProductId == itemVm.ProductId)
                    ?? throw new InvalidOperationException("GRN item not found.");

                var maxReturnable = grnItem.ReceivedQuantity - grnItem.ReturnedQuantity;
                if (itemVm.ReturnQuantity > maxReturnable)
                {
                    throw new InvalidOperationException($"Cannot return {itemVm.ReturnQuantity} units of product ID {grnItem.ProductId}. Maximum returnable quantity from {grn.GRNNumber} is {maxReturnable}.");
                }

                var product = await _context.Products.FindAsync(grnItem.ProductId)
                    ?? throw new InvalidOperationException("Product not found.");

                if (!allowNegative && product.CurrentStock < itemVm.ReturnQuantity)
                {
                    throw new InvalidOperationException($"Insufficient stock to return {itemVm.ReturnQuantity} of '{product.Name}'. Current stock is {product.CurrentStock}.");
                }

                var refundAmount = itemVm.ReturnQuantity * grnItem.CostPrice;
                totalRefund += refundAmount;

                grnItem.ReturnedQuantity += itemVm.ReturnQuantity;
                product.CurrentStock -= itemVm.ReturnQuantity;
                product.UpdatedAt = DateTime.UtcNow;

                purchaseReturn.Items.Add(new PurchaseReturnItem
                {
                    GoodsReceiptItemId = grnItem.Id,
                    ProductId = product.Id,
                    ReturnQuantity = itemVm.ReturnQuantity,
                    UnitCost = grnItem.CostPrice,
                    RefundAmount = refundAmount,
                    Reason = itemVm.ItemReason ?? model.Reason
                });

                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Purchase Return",
                    ReferenceNumber = returnNumber,
                    QuantityIn = 0m,
                    QuantityOut = itemVm.ReturnQuantity,
                    BalanceAfter = product.CurrentStock,
                    UnitCost = grnItem.CostPrice,
                    Notes = $"Purchase Return against {grn.GRNNumber}: {model.Reason}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            purchaseReturn.TotalReturnAmount = totalRefund;
            _context.PurchaseReturns.Add(purchaseReturn);

            // Adjust Supplier Balance (decrease payable)
            supplier.CurrentBalance -= totalRefund;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "PurchaseReturn", "PurchaseReturn", purchaseReturn.Id.ToString(),
                null, $"Completed {purchaseReturn.ReturnNumber} against {grn.GRNNumber}. TotalReturn={totalRefund:N2}", ipAddress);

            await tx.CommitAsync();
            return purchaseReturn;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}
