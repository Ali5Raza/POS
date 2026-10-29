using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface ISalesService
{
    IQueryable<Sale> QuerySales(string? search = null, string? status = null, int? customerId = null, string? paymentMethod = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<Sale?> GetByIdAsync(int id);
    Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber);
    Task<Sale> CompleteSaleAsync(SaleViewModel model, int? userId, string username, bool canOverridePrice, bool canApplyHighDiscount, bool isAdmin = false, string? ipAddress = null);
    Task CancelSaleAsync(int saleId, string reason, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
    Task<HeldSale> HoldSaleAsync(HoldSaleViewModel model, int? userId);
    Task<List<HeldSale>> GetHeldSalesAsync();
    Task DeleteHeldSaleAsync(int id);
    Task<ReceiptViewModel> BuildReceiptViewModelAsync(int saleId, string? format = null, bool autoPrint = false);
}

public class SalesService : ISalesService
{
    private readonly ApplicationDbContext _context;
    private readonly ICustomerService _customerService;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public SalesService(
        ApplicationDbContext context,
        ICustomerService customerService,
        ISettingsService settingsService,
        IDayClosingService dayClosingService,
        IAuditService auditService)
    {
        _context = context;
        _customerService = customerService;
        _settingsService = settingsService;
        _dayClosingService = dayClosingService;
        _auditService = auditService;
    }

    public IQueryable<Sale> QuerySales(string? search = null, string? status = null, int? customerId = null, string? paymentMethod = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        if (customerId.HasValue && customerId.Value > 0)
            query = query.Where(s => s.CustomerId == customerId.Value);

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            query = query.Where(s => s.PaymentMethod == paymentMethod);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(sale =>
                sale.InvoiceNumber.ToLower().Contains(s) ||
                (sale.Customer != null && sale.Customer.Name.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(s => s.SaleDate >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(s => s.SaleDate < end);
        }

        return query.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.Id);
    }

    public async Task<Sale?> GetByIdAsync(int id)
    {
        return await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.SalesReturns)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) return null;
        var norm = invoiceNumber.Trim().ToLower();
        return await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.InvoiceNumber.ToLower() == norm);
    }

    public async Task<Sale> CompleteSaleAsync(
        SaleViewModel model,
        int? userId,
        string username,
        bool canOverridePrice,
        bool canApplyHighDiscount,
        bool isAdmin = false,
        string? ipAddress = null)
    {
        await _dayClosingService.EnsureDateOpenForTransactionAsync(DateTime.UtcNow, isAdmin);

        var validItems = model.Items.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
        if (!validItems.Any())
            throw new InvalidOperationException("Cannot complete sale: cart is empty or quantities are invalid.");

        var allowNegativeStock = await _settingsService.GetBoolAsync("AllowNegativeStock", false);
        var maxDiscountPercent = await _settingsService.GetDecimalAsync("MaximumDiscountPercent", 20m);
        var requireCustForCredit = await _settingsService.GetBoolAsync("RequireCustomerForCreditSale", true);
        var enableTax = await _settingsService.GetBoolAsync("EnableTax", true);

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            Customer customer;
            if (model.CustomerId.HasValue && model.CustomerId.Value > 0)
            {
                customer = await _context.Customers.FindAsync(model.CustomerId.Value)
                    ?? await _customerService.GetOrCreateWalkInCustomerAsync();
            }
            else
            {
                customer = await _customerService.GetOrCreateWalkInCustomerAsync();
            }

            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };
            var seq = await _context.Sales.CountAsync() + 1;
            var invoiceNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_INV", "INV", seq);

            bool isWholesale = string.Equals(model.PriceTier, "Wholesale", StringComparison.OrdinalIgnoreCase);

            var sale = new Sale
            {
                InvoiceNumber = invoiceNumber,
                SaleDate = DateTime.UtcNow,
                CustomerId = customer.Id,
                IsWalkIn = customer.IsWalkIn,
                CashierId = userId,
                WarehouseId = warehouse.Id,
                PriceTier = isWholesale ? "Wholesale" : "Retail",
                PaymentMethod = string.IsNullOrWhiteSpace(model.PaymentMethod) ? "Cash" : model.PaymentMethod,
                Status = "Completed",
                Notes = model.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            decimal subTotal = 0m;
            decimal totalItemDiscounts = 0m;
            decimal totalTax = 0m;

            foreach (var itemVm in validItems)
            {
                var product = await _context.Products.FindAsync(itemVm.ProductId)
                    ?? throw new InvalidOperationException($"Product ID {itemVm.ProductId} does not exist.");

                if (!product.IsActive)
                    throw new InvalidOperationException($"Product '{product.Name}' is inactive and cannot be sold.");

                // 1. Validate Stock
                if (!allowNegativeStock && product.CurrentStock < itemVm.Quantity)
                {
                    throw new InvalidOperationException($"Insufficient stock for '{product.Name}'. Available: {product.CurrentStock}, Requested: {itemVm.Quantity}.");
                }

                // 2. Determine Standard Price & Check Price Override Permission
                var standardPrice = isWholesale && product.WholesalePrice > 0 ? product.WholesalePrice : product.SalePrice;
                var effectivePrice = itemVm.UnitPrice > 0 ? itemVm.UnitPrice : standardPrice;
                bool isOverridden = Math.Abs(effectivePrice - standardPrice) > 0.009m;

                if (isOverridden && !canOverridePrice)
                {
                    throw new UnauthorizedAccessException($"You do not have permission to override the price of '{product.Name}'.");
                }

                if (effectivePrice < product.MinimumSalePrice)
                {
                    if (!canOverridePrice)
                    {
                        throw new UnauthorizedAccessException($"Cannot sell '{product.Name}' at {effectivePrice:N2} below minimum sale price ({product.MinimumSalePrice:N2}).");
                    }
                    await _auditService.LogAsync(userId, username, "BelowMinimumPriceSale", "Product", product.Id.ToString(),
                        $"MinPrice={product.MinimumSalePrice:N2}", $"Sold at {effectivePrice:N2} on {invoiceNumber}", ipAddress);
                }
                else if (isOverridden)
                {
                    await _auditService.LogAsync(userId, username, "OverridePrice", "Product", product.Id.ToString(),
                        $"StandardPrice={standardPrice:N2}", $"OverriddenPrice={effectivePrice:N2} on {invoiceNumber}", ipAddress);
                }

                var lineGross = itemVm.Quantity * effectivePrice;
                if (itemVm.DiscountAmount < 0 || itemVm.DiscountAmount > lineGross)
                    throw new InvalidOperationException($"Invalid discount amount on '{product.Name}'.");

                // 3. Check item discount threshold
                if (lineGross > 0 && itemVm.DiscountAmount > 0)
                {
                    var itemDiscPct = (itemVm.DiscountAmount / lineGross) * 100m;
                    if (itemDiscPct > maxDiscountPercent && !canApplyHighDiscount)
                    {
                        throw new UnauthorizedAccessException($"Item discount of {itemDiscPct:N1}% on '{product.Name}' exceeds maximum allowed {maxDiscountPercent}%. Supervisor permission required.");
                    }
                    if (itemDiscPct > maxDiscountPercent)
                    {
                        await _auditService.LogAsync(userId, username, "HighDiscount", "Sale", invoiceNumber,
                            $"MaxAllowed={maxDiscountPercent}%", $"Applied {itemDiscPct:N1}% item discount on {product.Name}", ipAddress);
                    }
                }

                var taxableLine = Math.Max(0, lineGross - itemVm.DiscountAmount);
                var taxRate = enableTax ? (itemVm.TaxRate >= 0 ? itemVm.TaxRate : product.TaxRate) : 0m;
                var lineTax = Math.Round(taxableLine * (taxRate / 100m), 2);
                var lineTotal = taxableLine + lineTax;

                subTotal += lineGross;
                totalItemDiscounts += itemVm.DiscountAmount;
                totalTax += lineTax;

                sale.Items.Add(new SaleItem
                {
                    ProductId = product.Id,
                    Quantity = itemVm.Quantity,
                    ReturnedQuantity = 0m,
                    UnitCost = product.CostPrice,
                    UnitPrice = effectivePrice,
                    OriginalUnitPrice = standardPrice,
                    IsPriceOverridden = isOverridden,
                    DiscountAmount = itemVm.DiscountAmount,
                    TaxRate = taxRate,
                    TaxAmount = lineTax,
                    LineTotal = lineTotal
                });

                // 4. Decrease Stock
                product.CurrentStock -= itemVm.Quantity;
                product.UpdatedAt = DateTime.UtcNow;

                // 5. Create Stock Ledger
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Sale",
                    ReferenceNumber = invoiceNumber,
                    QuantityIn = 0m,
                    QuantityOut = itemVm.Quantity,
                    BalanceAfter = product.CurrentStock,
                    UnitCost = product.CostPrice,
                    Notes = $"POS Sale to {customer.Name}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Check invoice-level discount
            var totalDiscount = totalItemDiscounts + Math.Max(0, model.InvoiceDiscountAmount);
            if (subTotal > 0 && totalDiscount > 0)
            {
                var totalDiscPct = (totalDiscount / subTotal) * 100m;
                if (totalDiscPct > maxDiscountPercent && !canApplyHighDiscount)
                {
                    throw new UnauthorizedAccessException($"Total invoice discount ({totalDiscPct:N1}%) exceeds the maximum allowed {maxDiscountPercent}%. Supervisor permission is required.");
                }
                if (totalDiscPct > maxDiscountPercent)
                {
                    await _auditService.LogAsync(userId, username, "HighDiscount", "Sale", invoiceNumber,
                        $"MaxAllowed={maxDiscountPercent}%", $"Applied {totalDiscPct:N1}% total discount on {invoiceNumber}", ipAddress);
                }
            }

            sale.SubTotal = subTotal;
            sale.DiscountAmount = totalDiscount;
            sale.TaxAmount = totalTax;
            sale.TotalAmount = Math.Max(0, Math.Round(subTotal - totalDiscount + totalTax, 2));

            // Payment & Due calculations
            var paid = Math.Max(0, model.PaidAmount);
            if (string.Equals(sale.PaymentMethod, "Credit", StringComparison.OrdinalIgnoreCase) && paid == 0)
            {
                sale.PaidAmount = 0m;
                sale.ChangeAmount = 0m;
                sale.DueAmount = sale.TotalAmount;
            }
            else if (paid >= sale.TotalAmount)
            {
                sale.PaidAmount = paid;
                sale.ChangeAmount = paid - sale.TotalAmount;
                sale.DueAmount = 0m;
            }
            else
            {
                sale.PaidAmount = paid;
                sale.ChangeAmount = 0m;
                sale.DueAmount = sale.TotalAmount - paid;
            }

            // Validate Credit Sale rules
            if (sale.DueAmount > 0)
            {
                if (requireCustForCredit && customer.IsWalkIn)
                {
                    throw new InvalidOperationException("Walk-in customer cannot make a credit or partial-payment purchase. Please select a registered customer or collect full payment.");
                }

                if (!customer.IsWalkIn && customer.CreditLimit > 0 && (customer.CurrentBalance + sale.DueAmount) > customer.CreditLimit)
                {
                    throw new InvalidOperationException($"Credit limit exceeded for '{customer.Name}'. Credit Limit: {customer.CreditLimit:N2}, Current Balance: {customer.CurrentBalance:N2}, New Due: {sale.DueAmount:N2}.");
                }

                customer.CurrentBalance += sale.DueAmount;
            }

            _context.Sales.Add(sale);
            await _context.SaveChangesAsync();

            // 6. Create Payment record for net collected amount
            var netPaidTowardsInvoice = Math.Min(sale.TotalAmount, sale.PaidAmount);
            if (netPaidTowardsInvoice > 0)
            {
                var paySeq = await _context.Payments.CountAsync() + 1;
                var payNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_PAY", "PAY", paySeq);
                _context.Payments.Add(new Payment
                {
                    PaymentNumber = payNumber,
                    PaymentDate = sale.SaleDate,
                    PaymentType = "SaleReceipt",
                    Amount = netPaidTowardsInvoice,
                    PaymentMethod = sale.PaymentMethod,
                    ReferenceNumber = sale.InvoiceNumber,
                    CustomerId = customer.Id,
                    SaleId = sale.Id,
                    Notes = $"Payment collected for Invoice {sale.InvoiceNumber}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Remove held sale if applicable
            if (model.HeldSaleId.HasValue && model.HeldSaleId.Value > 0)
            {
                var held = await _context.HeldSales.FindAsync(model.HeldSaleId.Value);
                if (held != null) _context.HeldSales.Remove(held);
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "CompleteSale", "Sale", sale.Id.ToString(),
                null, $"Completed Sale {sale.InvoiceNumber}: Total={sale.TotalAmount:N2}, Paid={sale.PaidAmount:N2}, Due={sale.DueAmount:N2} ({sale.PaymentMethod})", ipAddress);

            await tx.CommitAsync();
            return sale;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task CancelSaleAsync(int saleId, string reason, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
    {
        var sale = await _context.Sales
            .Include(s => s.Items)
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("Sale invoice not found.");

        if (sale.Status == "Cancelled")
            throw new InvalidOperationException("Sale invoice is already cancelled.");

        await _dayClosingService.EnsureDateOpenForTransactionAsync(sale.SaleDate, isAdmin);

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };

            foreach (var item in sale.Items)
            {
                var remainingQty = item.Quantity - item.ReturnedQuantity;
                if (remainingQty > 0)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product != null)
                    {
                        product.CurrentStock += remainingQty;
                        product.UpdatedAt = DateTime.UtcNow;

                        _context.StockMovements.Add(new StockMovement
                        {
                            ProductId = product.Id,
                            WarehouseId = warehouse.Id,
                            TransactionType = "Sales Return",
                            ReferenceNumber = $"VOID-{sale.InvoiceNumber}",
                            QuantityIn = remainingQty,
                            QuantityOut = 0m,
                            BalanceAfter = product.CurrentStock,
                            UnitCost = item.UnitCost,
                            Notes = $"Voided Sale {sale.InvoiceNumber}: {reason}",
                            UserId = userId,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            if (sale.DueAmount > 0 && sale.Customer != null && !sale.Customer.IsWalkIn)
            {
                sale.Customer.CurrentBalance = Math.Max(0, sale.Customer.CurrentBalance - sale.DueAmount);
            }

            sale.Status = "Cancelled";
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes)
                ? $"Cancelled: {reason}"
                : $"{sale.Notes} | Cancelled: {reason}";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "CancelSale", "Sale", sale.Id.ToString(),
                "Completed", $"Cancelled {sale.InvoiceNumber}. Reason: {reason}", ipAddress);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<HeldSale> HoldSaleAsync(HoldSaleViewModel model, int? userId)
    {
        var count = await _context.HeldSales.CountAsync() + 1;
        var held = new HeldSale
        {
            HoldReference = $"HOLD-{DateTime.UtcNow:HHmmss}-{count:D2}",
            CustomerId = model.CustomerId,
            CustomerName = string.IsNullOrWhiteSpace(model.CustomerName) ? "Walk-in Customer" : model.CustomerName,
            CartJson = model.CartJson,
            TotalAmount = model.TotalAmount,
            Notes = model.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.HeldSales.Add(held);
        await _context.SaveChangesAsync();
        return held;
    }

    public async Task<List<HeldSale>> GetHeldSalesAsync()
    {
        return await _context.HeldSales
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();
    }

    public async Task DeleteHeldSaleAsync(int id)
    {
        var held = await _context.HeldSales.FindAsync(id);
        if (held != null)
        {
            _context.HeldSales.Remove(held);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<ReceiptViewModel> BuildReceiptViewModelAsync(int saleId, string? format = null, bool autoPrint = false)
    {
        var sale = await GetByIdAsync(saleId)
            ?? throw new InvalidOperationException("Sale invoice not found.");

        var settings = await _settingsService.GetSettingsViewModelAsync();
        return new ReceiptViewModel
        {
            Sale = sale,
            BusinessName = settings.BusinessName,
            BusinessAddress = settings.BusinessAddress,
            BusinessPhone = settings.BusinessPhone,
            BusinessEmail = settings.BusinessEmail,
            BusinessLogo = settings.BusinessLogo,
            TaxNumber = settings.TaxNumber,
            InvoiceFooter = settings.InvoiceFooter,
            Format = !string.IsNullOrWhiteSpace(format) ? format : settings.ReceiptFormat,
            AutoPrint = autoPrint || settings.AutoPrint
        };
    }
}

public interface ISalesReturnService
{
    IQueryable<SalesReturn> QuerySalesReturns(string? search = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<SalesReturn?> GetByIdAsync(int id);
    Task<SalesReturnViewModel> BuildReturnViewModelAsync(int saleId);
    Task<SalesReturn> CreateSalesReturnAsync(SalesReturnViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null);
}

public class SalesReturnService : ISalesReturnService
{
    private readonly ApplicationDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IAuditService _auditService;

    public SalesReturnService(
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

    public IQueryable<SalesReturn> QuerySalesReturns(string? search = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.SalesReturns
            .AsNoTracking()
            .Include(sr => sr.Sale)
            .Include(sr => sr.Customer)
            .Include(sr => sr.CreatedByUser)
            .Include(sr => sr.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(sr =>
                sr.ReturnNumber.ToLower().Contains(s) ||
                (sr.Sale != null && sr.Sale.InvoiceNumber.ToLower().Contains(s)) ||
                (sr.Customer != null && sr.Customer.Name.ToLower().Contains(s)));
        }

        if (fromDate.HasValue)
            query = query.Where(sr => sr.ReturnDate >= fromDate.Value.Date);

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(sr => sr.ReturnDate < end);
        }

        return query.OrderByDescending(sr => sr.ReturnDate).ThenByDescending(sr => sr.Id);
    }

    public async Task<SalesReturn?> GetByIdAsync(int id)
    {
        return await _context.SalesReturns
            .Include(sr => sr.Sale)
            .Include(sr => sr.Customer)
            .Include(sr => sr.CreatedByUser)
            .Include(sr => sr.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(sr => sr.Id == id);
    }

    public async Task<SalesReturnViewModel> BuildReturnViewModelAsync(int saleId)
    {
        var sale = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("Original sale invoice not found.");

        return new SalesReturnViewModel
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            CustomerId = sale.CustomerId,
            CustomerName = sale.Customer?.Name ?? "Walk-in Customer",
            ReturnDate = DateTime.UtcNow,
            RefundMethod = "Cash",
            Items = sale.Items.Select(i => new SalesReturnItemViewModel
            {
                SaleItemId = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? "",
                SKU = i.Product?.SKU ?? "",
                SoldQuantity = i.Quantity,
                AlreadyReturnedQuantity = i.ReturnedQuantity,
                UnitPrice = i.UnitPrice,
                TaxRate = i.TaxRate,
                ReturnQuantity = 0
            }).ToList()
        };
    }

    public async Task<SalesReturn> CreateSalesReturnAsync(SalesReturnViewModel model, int? userId, string username, bool isAdmin = false, string? ipAddress = null)
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
            var sale = await _context.Sales
                .Include(s => s.Items)
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.Id == model.SaleId)
                ?? throw new InvalidOperationException("Original Sale Invoice not found.");

            if (sale.Status == "Cancelled")
                throw new InvalidOperationException("Cannot return items from a cancelled invoice.");

            var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };
            var seq = await _context.SalesReturns.CountAsync() + 1;
            var returnNumber = await _settingsService.GenerateDocumentNumberAsync("Prefix_SR", "SR", seq);

            var salesReturn = new SalesReturn
            {
                ReturnNumber = returnNumber,
                SaleId = sale.Id,
                CustomerId = sale.CustomerId,
                ReturnDate = model.ReturnDate,
                Reason = model.Reason.Trim(),
                RefundMethod = string.IsNullOrWhiteSpace(model.RefundMethod) ? "Cash" : model.RefundMethod,
                Status = "Completed",
                Notes = model.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            decimal subTotalRefund = 0m;
            decimal taxRefund = 0m;

            foreach (var itemVm in itemsToReturn)
            {
                var saleItem = sale.Items.FirstOrDefault(si => si.Id == itemVm.SaleItemId || si.ProductId == itemVm.ProductId)
                    ?? throw new InvalidOperationException("Original sale line item not found.");

                var maxReturnable = saleItem.Quantity - saleItem.ReturnedQuantity;
                if (itemVm.ReturnQuantity > maxReturnable)
                {
                    throw new InvalidOperationException($"Cannot return {itemVm.ReturnQuantity} units of product ID {saleItem.ProductId}. Maximum returnable quantity is {maxReturnable}.");
                }

                var product = await _context.Products.FindAsync(saleItem.ProductId)
                    ?? throw new InvalidOperationException("Product not found.");

                // Calculate proportional net price & tax for returned units
                var unitDiscount = saleItem.Quantity > 0 ? (saleItem.DiscountAmount / saleItem.Quantity) : 0m;
                var netUnit = Math.Max(0, saleItem.UnitPrice - unitDiscount);
                var lineSub = Math.Round(netUnit * itemVm.ReturnQuantity, 2);
                var lineTax = Math.Round(lineSub * (saleItem.TaxRate / 100m), 2);
                var lineRefund = lineSub + lineTax;

                subTotalRefund += lineSub;
                taxRefund += lineTax;

                saleItem.ReturnedQuantity += itemVm.ReturnQuantity;
                product.CurrentStock += itemVm.ReturnQuantity;
                product.UpdatedAt = DateTime.UtcNow;

                salesReturn.Items.Add(new SalesReturnItem
                {
                    SaleItemId = saleItem.Id,
                    ProductId = product.Id,
                    ReturnQuantity = itemVm.ReturnQuantity,
                    UnitPrice = saleItem.UnitPrice,
                    DiscountAmount = Math.Round(unitDiscount * itemVm.ReturnQuantity, 2),
                    TaxAmount = lineTax,
                    RefundAmount = lineRefund,
                    Reason = itemVm.ItemReason ?? model.Reason
                });

                // Create Stock Movement
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Sales Return",
                    ReferenceNumber = returnNumber,
                    QuantityIn = itemVm.ReturnQuantity,
                    QuantityOut = 0m,
                    BalanceAfter = product.CurrentStock,
                    UnitCost = product.CostPrice,
                    Notes = $"Sales Return against {sale.InvoiceNumber}: {model.Reason}",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var totalRefund = Math.Round(subTotalRefund + taxRefund, 2);
            salesReturn.SubTotal = subTotalRefund;
            salesReturn.TaxRefund = taxRefund;
            salesReturn.TotalRefundAmount = totalRefund;
            salesReturn.PaidRefundAmount = totalRefund;

            _context.SalesReturns.Add(salesReturn);

            // Update Sale Status
            bool allReturned = sale.Items.All(i => i.ReturnedQuantity >= i.Quantity);
            sale.Status = allReturned ? "Returned" : "PartiallyReturned";

            await _context.SaveChangesAsync();

            // Update Customer Balance or Create Refund Payment
            if (sale.Customer != null && !sale.Customer.IsWalkIn &&
                (salesReturn.RefundMethod == "CustomerCredit" || sale.DueAmount > 0))
            {
                var creditReduce = Math.Min(sale.Customer.CurrentBalance, totalRefund);
                sale.Customer.CurrentBalance -= creditReduce;
                sale.DueAmount = Math.Max(0, sale.DueAmount - creditReduce);

                var cashPortion = totalRefund - creditReduce;
                if (cashPortion > 0 && salesReturn.RefundMethod != "CustomerCredit")
                {
                    var paySeq = await _context.Payments.CountAsync() + 1;
                    var payNum = await _settingsService.GenerateDocumentNumberAsync("Prefix_PAY", "PAY", paySeq);
                    _context.Payments.Add(new Payment
                    {
                        PaymentNumber = payNum,
                        PaymentDate = model.ReturnDate,
                        PaymentType = "SalesRefund",
                        Amount = cashPortion,
                        PaymentMethod = salesReturn.RefundMethod,
                        ReferenceNumber = returnNumber,
                        CustomerId = sale.CustomerId,
                        SaleId = sale.Id,
                        SalesReturnId = salesReturn.Id,
                        Notes = $"Sales Return Refund ({returnNumber})",
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else
            {
                var paySeq = await _context.Payments.CountAsync() + 1;
                var payNum = await _settingsService.GenerateDocumentNumberAsync("Prefix_PAY", "PAY", paySeq);
                _context.Payments.Add(new Payment
                {
                    PaymentNumber = payNum,
                    PaymentDate = model.ReturnDate,
                    PaymentType = "SalesRefund",
                    Amount = totalRefund,
                    PaymentMethod = salesReturn.RefundMethod,
                    ReferenceNumber = returnNumber,
                    CustomerId = sale.CustomerId,
                    SaleId = sale.Id,
                    SalesReturnId = salesReturn.Id,
                    Notes = $"Sales Return Refund ({returnNumber})",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userId, username, "SalesReturn", "SalesReturn", salesReturn.Id.ToString(),
                null, $"Completed {salesReturn.ReturnNumber} against {sale.InvoiceNumber}. Refund={totalRefund:N2} ({salesReturn.RefundMethod})", ipAddress);

            await tx.CommitAsync();
            return salesReturn;
        }
        catch
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}
