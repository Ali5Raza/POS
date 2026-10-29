using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.DTOs;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface IProductService
{
    IQueryable<Product> QueryProducts(string? search = null, int? categoryId = null, string? stockFilter = null, bool? activeOnly = null);
    Task<Product?> GetByIdAsync(int id);
    Task<Product?> FindByBarcodeOrSkuAsync(string code);
    Task<List<ProductLookupDto>> SearchPosProductsAsync(string? term, int? categoryId, int maxResults = 60);
    Task<Product> CreateProductAsync(ProductCreateViewModel model, int? userId, string username, string? ipAddress = null);
    Task<Product> UpdateProductAsync(ProductEditViewModel model, int? userId, string username, string? ipAddress = null);
    Task ToggleProductActiveAsync(int id, int? userId, string username, string? ipAddress = null);
}

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public ProductService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public IQueryable<Product> QueryProducts(string? search = null, int? categoryId = null, string? stockFilter = null, bool? activeOnly = null)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .AsQueryable();

        if (activeOnly.HasValue)
        {
            query = query.Where(p => p.IsActive == activeOnly.Value);
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(s) ||
                p.SKU.ToLower().Contains(s) ||
                p.Barcode.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(stockFilter))
        {
            switch (stockFilter.ToLower())
            {
                case "low":
                    query = query.Where(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel);
                    break;
                case "out":
                    query = query.Where(p => p.CurrentStock <= 0);
                    break;
                case "reorder":
                    query = query.Where(p => p.CurrentStock <= p.ReorderLevel);
                    break;
                case "instock":
                    query = query.Where(p => p.CurrentStock > 0);
                    break;
            }
        }

        return query.OrderBy(p => p.Name);
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product?> FindByBarcodeOrSkuAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.IsActive &&
                (p.Barcode.ToLower() == trimmed.ToLower() || p.SKU.ToLower() == trimmed.ToLower()));
    }

    public async Task<List<ProductLookupDto>> SearchPosProductsAsync(string? term, int? categoryId, int maxResults = 60)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var s = term.Trim().ToLower();
            query = query.Where(p =>
                p.Barcode.ToLower() == s ||
                p.SKU.ToLower() == s ||
                p.Name.ToLower().Contains(s) ||
                p.SKU.ToLower().Contains(s) ||
                p.Barcode.ToLower().Contains(s));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(maxResults)
            .Select(p => new ProductLookupDto
            {
                Id = p.Id,
                SKU = p.SKU,
                Barcode = p.Barcode,
                Name = p.Name,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : "",
                CostPrice = p.CostPrice,
                SalePrice = p.SalePrice,
                WholesalePrice = p.WholesalePrice > 0 ? p.WholesalePrice : p.SalePrice,
                MinimumSalePrice = p.MinimumSalePrice,
                TaxRate = p.TaxRate,
                Unit = p.Unit,
                CurrentStock = p.CurrentStock,
                MinimumStockLevel = p.MinimumStockLevel
            })
            .ToListAsync();
    }

    public async Task<Product> CreateProductAsync(ProductCreateViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var sku = model.SKU.Trim();
        var barcode = model.Barcode.Trim();

        if (await _context.Products.AnyAsync(p => p.SKU.ToLower() == sku.ToLower()))
            throw new InvalidOperationException($"A product with SKU '{sku}' already exists.");

        if (await _context.Products.AnyAsync(p => p.Barcode.ToLower() == barcode.ToLower()))
            throw new InvalidOperationException($"A product with Barcode '{barcode}' already exists.");

        if (model.SalePrice <= 0)
            throw new InvalidOperationException("Sale price must be greater than zero.");

        if (model.CostPrice < 0)
            throw new InvalidOperationException("Cost price cannot be negative.");

        if (model.OpeningStock < 0)
            throw new InvalidOperationException("Opening stock cannot be negative.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var product = new Product
            {
                SKU = sku,
                Barcode = barcode,
                Name = model.Name.Trim(),
                Description = model.Description?.Trim(),
                CategoryId = model.CategoryId,
                DefaultSupplierId = model.DefaultSupplierId,
                CostPrice = model.CostPrice,
                SalePrice = model.SalePrice,
                WholesalePrice = model.WholesalePrice > 0 ? model.WholesalePrice : model.SalePrice,
                MinimumSalePrice = model.MinimumSalePrice > 0 ? model.MinimumSalePrice : model.CostPrice,
                TaxRate = model.TaxRate,
                Unit = string.IsNullOrWhiteSpace(model.Unit) ? "pcs" : model.Unit.Trim(),
                OpeningStock = model.OpeningStock,
                CurrentStock = model.OpeningStock,
                MinimumStockLevel = model.MinimumStockLevel,
                ReorderLevel = model.ReorderLevel,
                IsActive = model.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            if (model.OpeningStock > 0)
            {
                var warehouse = await _context.Warehouses.FirstOrDefaultAsync() ?? new Warehouse { Id = 1 };
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Opening Stock",
                    ReferenceNumber = $"OPEN-{product.SKU}",
                    QuantityIn = model.OpeningStock,
                    QuantityOut = 0,
                    BalanceAfter = model.OpeningStock,
                    UnitCost = model.CostPrice,
                    Notes = "Initial opening stock on product creation",
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            await _auditService.LogAsync(userId, username, "CreateProduct", "Product", product.Id.ToString(),
                null, $"Created product {product.SKU} - {product.Name} (Price: {product.SalePrice:N2}, OpeningStock: {product.OpeningStock})", ipAddress);

            await tx.CommitAsync();
            return product;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<Product> UpdateProductAsync(ProductEditViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var product = await _context.Products.FindAsync(model.Id)
            ?? throw new InvalidOperationException("Product not found.");

        var sku = model.SKU.Trim();
        var barcode = model.Barcode.Trim();

        if (await _context.Products.AnyAsync(p => p.Id != model.Id && p.SKU.ToLower() == sku.ToLower()))
            throw new InvalidOperationException($"Another product with SKU '{sku}' already exists.");

        if (await _context.Products.AnyAsync(p => p.Id != model.Id && p.Barcode.ToLower() == barcode.ToLower()))
            throw new InvalidOperationException($"Another product with Barcode '{barcode}' already exists.");

        var oldSummary = $"Name={product.Name}, Cost={product.CostPrice:N2}, Sale={product.SalePrice:N2}, Wholesale={product.WholesalePrice:N2}, MinPrice={product.MinimumSalePrice:N2}, Active={product.IsActive}";
        bool priceChanged = product.SalePrice != model.SalePrice || product.CostPrice != model.CostPrice || product.MinimumSalePrice != model.MinimumSalePrice;

        product.SKU = sku;
        product.Barcode = barcode;
        product.Name = model.Name.Trim();
        product.Description = model.Description?.Trim();
        product.CategoryId = model.CategoryId;
        product.DefaultSupplierId = model.DefaultSupplierId;
        product.CostPrice = model.CostPrice;
        product.SalePrice = model.SalePrice;
        product.WholesalePrice = model.WholesalePrice > 0 ? model.WholesalePrice : model.SalePrice;
        product.MinimumSalePrice = model.MinimumSalePrice;
        product.TaxRate = model.TaxRate;
        product.Unit = string.IsNullOrWhiteSpace(model.Unit) ? "pcs" : model.Unit.Trim();
        product.MinimumStockLevel = model.MinimumStockLevel;
        product.ReorderLevel = model.ReorderLevel;
        product.IsActive = model.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var newSummary = $"Name={product.Name}, Cost={product.CostPrice:N2}, Sale={product.SalePrice:N2}, Wholesale={product.WholesalePrice:N2}, MinPrice={product.MinimumSalePrice:N2}, Active={product.IsActive}";
        await _auditService.LogAsync(userId, username, priceChanged ? "PriceChange" : "UpdateProduct", "Product", product.Id.ToString(), oldSummary, newSummary, ipAddress);

        return product;
    }

    public async Task ToggleProductActiveAsync(int id, int? userId, string username, string? ipAddress = null)
    {
        var product = await _context.Products.FindAsync(id)
            ?? throw new InvalidOperationException("Product not found.");

        var oldState = product.IsActive;
        product.IsActive = !product.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, product.IsActive ? "ActivateProduct" : "DeactivateProduct", "Product",
            product.Id.ToString(), $"IsActive={oldState}", $"IsActive={product.IsActive} ({product.SKU})", ipAddress);
    }
}

public interface ISupplierService
{
    IQueryable<Supplier> QuerySuppliers(string? search = null, bool? activeOnly = null);
    Task<Supplier?> GetByIdAsync(int id);
    Task<Supplier> CreateSupplierAsync(SupplierViewModel model, int? userId, string username, string? ipAddress = null);
    Task<Supplier> UpdateSupplierAsync(SupplierViewModel model, int? userId, string username, string? ipAddress = null);
    Task<SupplierDetailsViewModel> GetSupplierDetailsWithLedgerAsync(int id);
}

public class SupplierService : ISupplierService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public SupplierService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public IQueryable<Supplier> QuerySuppliers(string? search = null, bool? activeOnly = null)
    {
        var query = _context.Suppliers.AsNoTracking().AsQueryable();

        if (activeOnly.HasValue)
            query = query.Where(s => s.IsActive == activeOnly.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(sup =>
                sup.Name.ToLower().Contains(s) ||
                sup.CompanyName.ToLower().Contains(s) ||
                sup.Phone.ToLower().Contains(s) ||
                (sup.TaxNumber != null && sup.TaxNumber.ToLower().Contains(s)));
        }

        return query.OrderBy(s => s.CompanyName);
    }

    public async Task<Supplier?> GetByIdAsync(int id) =>
        await _context.Suppliers.FindAsync(id);

    public async Task<Supplier> CreateSupplierAsync(SupplierViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var supplier = new Supplier
        {
            Name = model.Name.Trim(),
            CompanyName = model.CompanyName.Trim(),
            Phone = model.Phone.Trim(),
            Email = model.Email?.Trim(),
            Address = model.Address?.Trim(),
            TaxNumber = model.TaxNumber?.Trim(),
            OpeningBalance = model.OpeningBalance,
            CurrentBalance = model.OpeningBalance,
            IsActive = model.IsActive,
            Notes = model.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "CreateSupplier", "Supplier", supplier.Id.ToString(),
            null, $"Created supplier {supplier.CompanyName} (OpeningBalance: {supplier.OpeningBalance:N2})", ipAddress);

        return supplier;
    }

    public async Task<Supplier> UpdateSupplierAsync(SupplierViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var supplier = await _context.Suppliers.FindAsync(model.Id)
            ?? throw new InvalidOperationException("Supplier not found.");

        var oldVal = $"{supplier.CompanyName} ({supplier.Phone})";
        var diffOpening = model.OpeningBalance - supplier.OpeningBalance;

        supplier.Name = model.Name.Trim();
        supplier.CompanyName = model.CompanyName.Trim();
        supplier.Phone = model.Phone.Trim();
        supplier.Email = model.Email?.Trim();
        supplier.Address = model.Address?.Trim();
        supplier.TaxNumber = model.TaxNumber?.Trim();
        supplier.OpeningBalance = model.OpeningBalance;
        supplier.CurrentBalance += diffOpening;
        supplier.IsActive = model.IsActive;
        supplier.Notes = model.Notes?.Trim();

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "UpdateSupplier", "Supplier", supplier.Id.ToString(),
            oldVal, $"{supplier.CompanyName} ({supplier.Phone})", ipAddress);

        return supplier;
    }

    public async Task<SupplierDetailsViewModel> GetSupplierDetailsWithLedgerAsync(int id)
    {
        var supplier = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Supplier not found.");

        var grns = await _context.GoodsReceipts
            .AsNoTracking()
            .Where(g => g.SupplierId == id && g.Status == "Completed")
            .OrderByDescending(g => g.ReceivingDate)
            .ToListAsync();

        var returns = await _context.PurchaseReturns
            .AsNoTracking()
            .Include(pr => pr.GoodsReceipt)
            .Where(pr => pr.SupplierId == id && pr.Status == "Completed")
            .OrderByDescending(pr => pr.ReturnDate)
            .ToListAsync();

        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.SupplierId == id)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        var rawEntries = new List<(DateTime Date, string Type, string Ref, string Desc, decimal Debit, decimal Credit)>();

        if (supplier.OpeningBalance != 0)
        {
            rawEntries.Add((supplier.CreatedAt.AddMinutes(-1), "Opening Balance", "OPEN-BAL", "Initial supplier payable balance", 0m, supplier.OpeningBalance));
        }

        foreach (var g in grns)
        {
            rawEntries.Add((g.ReceivingDate, "Goods Received (GRN)", g.GRNNumber, $"Purchase Invoice {g.SupplierInvoiceNumber ?? g.GRNNumber}", 0m, g.TotalAmount));
        }

        foreach (var pr in returns)
        {
            rawEntries.Add((pr.ReturnDate, "Purchase Return", pr.ReturnNumber, $"Return against {pr.GoodsReceipt?.GRNNumber}: {pr.Reason}", pr.TotalReturnAmount, 0m));
        }

        foreach (var pay in payments)
        {
            if (pay.PaymentType == "PurchaseRefund")
            {
                rawEntries.Add((pay.PaymentDate, "Supplier Refund", pay.PaymentNumber, pay.Notes ?? "Refund from supplier", 0m, pay.Amount));
            }
            else
            {
                rawEntries.Add((pay.PaymentDate, "Supplier Payment", pay.PaymentNumber, $"Paid via {pay.PaymentMethod} ({pay.Notes ?? ""})", pay.Amount, 0m));
            }
        }

        decimal running = 0m;
        var ledger = new List<LedgerEntryDto>();
        foreach (var e in rawEntries.OrderBy(x => x.Date))
        {
            // For supplier payable: Credit increases payable balance, Debit (payment/return) decreases payable balance
            running += (e.Credit - e.Debit);
            ledger.Add(new LedgerEntryDto
            {
                Date = e.Date,
                TransactionType = e.Type,
                ReferenceNumber = e.Ref,
                Description = e.Desc,
                Debit = e.Debit,
                Credit = e.Credit,
                RunningBalance = running
            });
        }

        return new SupplierDetailsViewModel
        {
            Supplier = supplier,
            Purchases = grns,
            PurchaseReturns = returns,
            Payments = payments,
            Ledger = ledger
        };
    }
}

public interface ICustomerService
{
    IQueryable<Customer> QueryCustomers(string? search = null, bool? activeOnly = null);
    Task<Customer?> GetByIdAsync(int id);
    Task<Customer> GetOrCreateWalkInCustomerAsync();
    Task<Customer> CreateCustomerAsync(CustomerViewModel model, int? userId, string username, string? ipAddress = null);
    Task<Customer> UpdateCustomerAsync(CustomerViewModel model, int? userId, string username, string? ipAddress = null);
    Task<CustomerDetailsViewModel> GetCustomerDetailsWithLedgerAsync(int id);
}

public class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public CustomerService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public IQueryable<Customer> QueryCustomers(string? search = null, bool? activeOnly = null)
    {
        var query = _context.Customers.AsNoTracking().AsQueryable();

        if (activeOnly.HasValue)
            query = query.Where(c => c.IsActive == activeOnly.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(s) ||
                (c.Phone != null && c.Phone.ToLower().Contains(s)) ||
                (c.Email != null && c.Email.ToLower().Contains(s)));
        }

        return query.OrderByDescending(c => c.IsWalkIn).ThenBy(c => c.Name);
    }

    public async Task<Customer?> GetByIdAsync(int id) =>
        await _context.Customers.FindAsync(id);

    public async Task<Customer> GetOrCreateWalkInCustomerAsync()
    {
        var walkIn = await _context.Customers.FirstOrDefaultAsync(c => c.IsWalkIn || c.Name == "Walk-in Customer");
        if (walkIn == null)
        {
            walkIn = new Customer
            {
                Name = "Walk-in Customer",
                Phone = "000-0000000",
                Address = "Counter Retail Sale",
                IsWalkIn = true,
                IsActive = true
            };
            _context.Customers.Add(walkIn);
            await _context.SaveChangesAsync();
        }
        return walkIn;
    }

    public async Task<Customer> CreateCustomerAsync(CustomerViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var customer = new Customer
        {
            Name = model.Name.Trim(),
            Phone = model.Phone?.Trim(),
            Email = model.Email?.Trim(),
            Address = model.Address?.Trim(),
            OpeningBalance = model.OpeningBalance,
            CreditLimit = model.CreditLimit,
            CurrentBalance = model.OpeningBalance,
            IsWalkIn = false,
            IsActive = model.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "CreateCustomer", "Customer", customer.Id.ToString(),
            null, $"Created customer {customer.Name} (CreditLimit: {customer.CreditLimit:N2})", ipAddress);

        return customer;
    }

    public async Task<Customer> UpdateCustomerAsync(CustomerViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var customer = await _context.Customers.FindAsync(model.Id)
            ?? throw new InvalidOperationException("Customer not found.");

        var diffOpening = model.OpeningBalance - customer.OpeningBalance;
        var oldVal = $"{customer.Name} (CreditLimit={customer.CreditLimit:N2})";

        if (!customer.IsWalkIn)
        {
            customer.Name = model.Name.Trim();
            customer.OpeningBalance = model.OpeningBalance;
            customer.CurrentBalance += diffOpening;
            customer.CreditLimit = model.CreditLimit;
            customer.IsActive = model.IsActive;
        }

        customer.Phone = model.Phone?.Trim();
        customer.Email = model.Email?.Trim();
        customer.Address = model.Address?.Trim();

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(userId, username, "UpdateCustomer", "Customer", customer.Id.ToString(),
            oldVal, $"{customer.Name} (CreditLimit={customer.CreditLimit:N2})", ipAddress);

        return customer;
    }

    public async Task<CustomerDetailsViewModel> GetCustomerDetailsWithLedgerAsync(int id)
    {
        var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("Customer not found.");

        var sales = await _context.Sales
            .AsNoTracking()
            .Where(s => s.CustomerId == id && s.Status != "Cancelled")
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync();

        var returns = await _context.SalesReturns
            .AsNoTracking()
            .Include(sr => sr.Sale)
            .Where(sr => sr.CustomerId == id && sr.Status == "Completed")
            .OrderByDescending(sr => sr.ReturnDate)
            .ToListAsync();

        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.CustomerId == id)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        var rawEntries = new List<(DateTime Date, string Type, string Ref, string Desc, decimal Debit, decimal Credit)>();

        if (customer.OpeningBalance != 0)
        {
            rawEntries.Add((customer.CreatedAt.AddMinutes(-1), "Opening Balance", "OPEN-BAL", "Initial customer receivable balance", customer.OpeningBalance, 0m));
        }

        foreach (var s in sales)
        {
            rawEntries.Add((s.SaleDate, "Sale Invoice", s.InvoiceNumber, $"Retail Sale ({s.PaymentMethod})", s.TotalAmount, 0m));
        }

        foreach (var sr in returns)
        {
            rawEntries.Add((sr.ReturnDate, "Sales Return", sr.ReturnNumber, $"Return against {sr.Sale?.InvoiceNumber} ({sr.RefundMethod})", 0m, sr.TotalRefundAmount));
        }

        foreach (var pay in payments)
        {
            if (pay.PaymentType == "SalesRefund")
            {
                rawEntries.Add((pay.PaymentDate, "Cash Refund Paid", pay.PaymentNumber, $"Refund paid to customer ({pay.PaymentMethod})", pay.Amount, 0m));
            }
            else
            {
                rawEntries.Add((pay.PaymentDate, "Customer Payment", pay.PaymentNumber, $"Received via {pay.PaymentMethod}", 0m, pay.Amount));
            }
        }

        decimal running = 0m;
        var ledger = new List<LedgerEntryDto>();
        foreach (var e in rawEntries.OrderBy(x => x.Date))
        {
            // For customer receivable: Debit (Sale) increases balance, Credit (Payment/Return) decreases balance
            running += (e.Debit - e.Credit);
            ledger.Add(new LedgerEntryDto
            {
                Date = e.Date,
                TransactionType = e.Type,
                ReferenceNumber = e.Ref,
                Description = e.Desc,
                Debit = e.Debit,
                Credit = e.Credit,
                RunningBalance = running
            });
        }

        return new CustomerDetailsViewModel
        {
            Customer = customer,
            Sales = sales,
            SalesReturns = returns,
            Payments = payments,
            Ledger = ledger
        };
    }
}
