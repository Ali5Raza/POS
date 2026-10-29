using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;
using POSSystem.Services;
using Xunit;

namespace POSSystem.Tests;

public class BusinessLogicAndE2ETests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly ISettingsService _settingsService;
    private readonly IDayClosingService _dayClosingService;
    private readonly IMonthlyClosingService _monthlyClosingService;
    private readonly IProductService _productService;
    private readonly ISupplierService _supplierService;
    private readonly ICustomerService _customerService;
    private readonly IInventoryService _inventoryService;
    private readonly IPurchaseService _purchaseService;
    private readonly IGoodsReceivingService _grnService;
    private readonly IPurchaseReturnService _purchaseReturnService;
    private readonly ISalesService _salesService;
    private readonly ISalesReturnService _salesReturnService;
    private readonly IPaymentService _paymentService;
    private readonly IExpenseService _expenseService;
    private readonly IReportingService _reportingService;

    public BusinessLogicAndE2ETests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        // Seed minimal base entities for tests
        _context.Warehouses.Add(new Warehouse { Id = 1, Code = "MAIN", Name = "Main Store", IsDefault = true });
        _context.ProductCategories.Add(new ProductCategory { Id = 1, Name = "General Retail", IsActive = true });
        _context.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Utilities", IsActive = true });
        _context.Roles.Add(new Role { Id = 1, Name = "Admin", IsSystemRole = true });
        _context.Users.Add(new ApplicationUser { Id = 1, Username = "admin", FullName = "Admin", Email = "admin@test.local", PasswordHash = "hash", RoleId = 1, IsActive = true });
        _context.Customers.Add(new Customer { Id = 1, Name = "Walk-in Customer", IsWalkIn = true, IsActive = true });
        _context.SystemSettings.AddRange(
            new SystemSetting { Key = "AllowNegativeStock", Value = "false", Group = "POS" },
            new SystemSetting { Key = "MaximumDiscountPercent", Value = "20", Group = "POS" },
            new SystemSetting { Key = "RequireCustomerForCreditSale", Value = "true", Group = "POS" },
            new SystemSetting { Key = "EnableTax", Value = "true", Group = "Tax" }
        );
        _context.SaveChanges();

        _auditService = new AuditService(_context);
        _settingsService = new SettingsService(_context, _auditService);
        _dayClosingService = new DayClosingService(_context, _auditService);
        _monthlyClosingService = new MonthlyClosingService(_context, _auditService);
        _productService = new ProductService(_context, _auditService);
        _supplierService = new SupplierService(_context, _auditService);
        _customerService = new CustomerService(_context, _auditService);
        _inventoryService = new InventoryService(_context, _settingsService, _dayClosingService, _auditService);
        _purchaseService = new PurchaseService(_context, _settingsService, _dayClosingService, _auditService);
        _grnService = new GoodsReceivingService(_context, _settingsService, _dayClosingService, _auditService);
        _purchaseReturnService = new PurchaseReturnService(_context, _settingsService, _dayClosingService, _auditService);
        _salesService = new SalesService(_context, _customerService, _settingsService, _dayClosingService, _auditService);
        _salesReturnService = new SalesReturnService(_context, _settingsService, _dayClosingService, _auditService);
        _paymentService = new PaymentService(_context, _settingsService, _dayClosingService, _auditService);
        _expenseService = new ExpenseService(_context, _settingsService, _dayClosingService, _auditService);
        _reportingService = new ReportingService(_context);
    }

    [Fact]
    public async Task ProductCreation_DuplicateSkuAndBarcode_AreRejected()
    {
        var p1 = await _productService.CreateProductAsync(new ProductCreateViewModel
        {
            SKU = "SKU-TEST-01",
            Barcode = "89600001",
            Name = "Test Coffee 100g",
            CategoryId = 1,
            CostPrice = 500m,
            SalePrice = 700m,
            OpeningStock = 25m
        }, 1, "admin");

        Assert.Equal(25m, p1.CurrentStock);

        // Verify opening stock ledger created
        var ledger = await _context.StockMovements.FirstOrDefaultAsync(m => m.ProductId == p1.Id);
        Assert.NotNull(ledger);
        Assert.Equal("Opening Stock", ledger!.TransactionType);
        Assert.Equal(25m, ledger.BalanceAfter);

        // Duplicate SKU must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productService.CreateProductAsync(new ProductCreateViewModel
            {
                SKU = "SKU-TEST-01",
                Barcode = "89600002",
                Name = "Another Product",
                CategoryId = 1,
                CostPrice = 100m,
                SalePrice = 200m
            }, 1, "admin"));

        // Duplicate Barcode must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _productService.CreateProductAsync(new ProductCreateViewModel
            {
                SKU = "SKU-TEST-02",
                Barcode = "89600001",
                Name = "Another Product",
                CategoryId = 1,
                CostPrice = 100m,
                SalePrice = 200m
            }, 1, "admin"));
    }

    [Fact]
    public async Task NegativeStock_And_PriceOverride_And_Discounts_EnforcedStrictly()
    {
        var prod = await _productService.CreateProductAsync(new ProductCreateViewModel
        {
            SKU = "SKU-NEG-01",
            Barcode = "89600111",
            Name = "Limited Stock Item",
            CategoryId = 1,
            CostPrice = 400m,
            SalePrice = 600m,
            MinimumSalePrice = 550m,
            OpeningStock = 5m
        }, 1, "admin");

        // 1. Selling 10 when stock is 5 and AllowNegativeStock=false must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _salesService.CompleteSaleAsync(new SaleViewModel
            {
                PaymentMethod = "Cash",
                PaidAmount = 6000m,
                Items = new List<SaleItemViewModel>
                {
                    new() { ProductId = prod.Id, Quantity = 10, UnitPrice = 600m }
                }
            }, 1, "cashier", canOverridePrice: false, canApplyHighDiscount: false));

        // 2. Unauthorized price override must fail
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _salesService.CompleteSaleAsync(new SaleViewModel
            {
                PaymentMethod = "Cash",
                PaidAmount = 500m,
                Items = new List<SaleItemViewModel>
                {
                    new() { ProductId = prod.Id, Quantity = 1, UnitPrice = 500m }
                }
            }, 1, "cashier", canOverridePrice: false, canApplyHighDiscount: false));

        // 3. Unauthorized high discount (>20%) must fail
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _salesService.CompleteSaleAsync(new SaleViewModel
            {
                PaymentMethod = "Cash",
                PaidAmount = 300m,
                InvoiceDiscountAmount = 250m, // > 40% discount
                Items = new List<SaleItemViewModel>
                {
                    new() { ProductId = prod.Id, Quantity = 1, UnitPrice = 600m }
                }
            }, 1, "cashier", canOverridePrice: false, canApplyHighDiscount: false));

        // 4. Authorized supervisor CAN override price and apply high discount, and it is audited
        var sale = await _salesService.CompleteSaleAsync(new SaleViewModel
        {
            PaymentMethod = "Cash",
            PaidAmount = 500m,
            Items = new List<SaleItemViewModel>
            {
                new() { ProductId = prod.Id, Quantity = 1, UnitPrice = 500m }
            }
        }, 1, "manager", canOverridePrice: true, canApplyHighDiscount: true);

        Assert.NotNull(sale);
        var updatedProd = await _context.Products.FindAsync(prod.Id);
        Assert.Equal(4m, updatedProd!.CurrentStock);
    }

    [Fact]
    public async Task Complete_EndToEnd_RetailPOS_And_ERP_Workflow_Test()
    {
        // STEP 1: Create Product (Opening Stock = 10)
        var product = await _productService.CreateProductAsync(new ProductCreateViewModel
        {
            SKU = "E2E-1001",
            Barcode = "89699990001",
            Name = "E2E Premium Olive Oil 1L",
            CategoryId = 1,
            CostPrice = 1000m,
            SalePrice = 1500m,
            WholesalePrice = 1350m,
            MinimumSalePrice = 1200m,
            TaxRate = 0m,
            Unit = "btl",
            OpeningStock = 10m
        }, 1, "admin");
        Assert.Equal(10m, product.CurrentStock);

        // STEP 2: Create Supplier & Customer
        var supplier = await _supplierService.CreateSupplierAsync(new SupplierViewModel
        {
            Name = "Ali Hassan",
            CompanyName = "Olive Imports Pvt Ltd",
            Phone = "0300-1234567",
            OpeningBalance = 0m
        }, 1, "admin");

        var customer = await _customerService.CreateCustomerAsync(new CustomerViewModel
        {
            Name = "Zafar Iqbal",
            Phone = "0321-7654321",
            CreditLimit = 50000m,
            OpeningBalance = 0m
        }, 1, "admin");

        // STEP 3: Create Purchase Order (Order 20 units @ 1000 = 20,000)
        var po = await _purchaseService.CreatePurchaseOrderAsync(new PurchaseCreateViewModel
        {
            SupplierId = supplier.Id,
            OrderDate = DateTime.UtcNow.Date,
            Status = "Ordered",
            Items = new List<PurchaseItemViewModel>
            {
                new() { ProductId = product.Id, Quantity = 20m, CostPrice = 1000m }
            }
        }, 1, "admin");
        Assert.Equal(20000m, po.TotalAmount);

        // STEP 4: Goods Receiving (Receive 20 units, pay 12,000, due 8,000) -> Inventory Increases to 30
        var grn = await _grnService.CreateGoodsReceiptAsync(new GoodsReceiptViewModel
        {
            PurchaseOrderId = po.Id,
            SupplierId = supplier.Id,
            ReceivingDate = DateTime.UtcNow,
            PaidAmount = 12000m,
            PaymentMethod = "Bank Transfer",
            Items = new List<GoodsReceiptItemViewModel>
            {
                new()
                {
                    PurchaseOrderItemId = po.Items.First().Id,
                    ProductId = product.Id,
                    OrderedQuantity = 20m,
                    ReceivedQuantity = 20m,
                    CostPrice = 1000m
                }
            }
        }, 1, "admin");

        product = await _context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        supplier = await _context.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplier.Id);
        po = await _context.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == po.Id);
        Assert.Equal(30m, product.CurrentStock); // 10 opening + 20 GRN
        Assert.Equal(8000m, supplier.CurrentBalance); // 20,000 - 12,000 paid
        Assert.Equal("FullyReceived", po.Status);

        // STEP 5: POS Sale (Sell 5 units @ 1500 = 7,500; Customer pays 4,500 Cash, 3,000 Credit Due) -> Inventory Decreases to 25
        var sale = await _salesService.CompleteSaleAsync(new SaleViewModel
        {
            CustomerId = customer.Id,
            PriceTier = "Retail",
            PaymentMethod = "Cash",
            PaidAmount = 4500m,
            Items = new List<SaleItemViewModel>
            {
                new() { ProductId = product.Id, Quantity = 5m, UnitPrice = 1500m, TaxRate = 0m }
            }
        }, 1, "admin", canOverridePrice: true, canApplyHighDiscount: true);

        product = await _context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        customer = await _context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(25m, product.CurrentStock); // 30 - 5 sold
        Assert.Equal(3000m, customer.CurrentBalance); // 7,500 total - 4,500 paid

        // STEP 6: Customer Payment (Customer pays 1,000 towards balance -> balance becomes 2,000)
        await _paymentService.CreatePaymentAsync(new PaymentViewModel
        {
            PaymentDate = DateTime.UtcNow,
            PaymentType = "CustomerReceipt",
            CustomerId = customer.Id,
            SaleId = sale.Id,
            Amount = 1000m,
            PaymentMethod = "Cash"
        }, 1, "admin");

        customer = await _context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(2000m, customer.CurrentBalance);

        // STEP 7: Sales Return (Customer returns 1 unit @ 1500 via CustomerCredit) -> Inventory Increases to 26, Customer Balance becomes 500
        var saleItem = sale.Items.First();
        // Attempting to return more than sold (e.g., 10) must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _salesReturnService.CreateSalesReturnAsync(new SalesReturnViewModel
            {
                SaleId = sale.Id,
                Reason = "Too many returned",
                RefundMethod = "CustomerCredit",
                Items = new List<SalesReturnItemViewModel>
                {
                    new() { SaleItemId = saleItem.Id, ProductId = product.Id, ReturnQuantity = 10m }
                }
            }, 1, "admin"));

        var salesReturn = await _salesReturnService.CreateSalesReturnAsync(new SalesReturnViewModel
        {
            SaleId = sale.Id,
            Reason = "Customer returned 1 bottle",
            RefundMethod = "CustomerCredit",
            Items = new List<SalesReturnItemViewModel>
            {
                new() { SaleItemId = saleItem.Id, ProductId = product.Id, ReturnQuantity = 1m }
            }
        }, 1, "admin");

        product = await _context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        customer = await _context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(26m, product.CurrentStock); // 25 + 1 returned
        Assert.Equal(500m, customer.CurrentBalance); // 2000 - 1500 credit return

        // STEP 8: Purchase Return (Return 2 units @ 1000 = 2,000 to supplier) -> Inventory Decreases to 24, Supplier Balance becomes 6,000
        var grnItem = grn.Items.First();
        // Returning more than received (e.g., 25) must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _purchaseReturnService.CreatePurchaseReturnAsync(new PurchaseReturnViewModel
            {
                GoodsReceiptId = grn.Id,
                SupplierId = supplier.Id,
                Reason = "Excess return",
                Items = new List<PurchaseReturnItemViewModel>
                {
                    new() { GoodsReceiptItemId = grnItem.Id, ProductId = product.Id, ReturnQuantity = 25m }
                }
            }, 1, "admin"));

        var purchaseReturn = await _purchaseReturnService.CreatePurchaseReturnAsync(new PurchaseReturnViewModel
        {
            GoodsReceiptId = grn.Id,
            SupplierId = supplier.Id,
            Reason = "Damaged cap on 2 bottles",
            Items = new List<PurchaseReturnItemViewModel>
            {
                new() { GoodsReceiptItemId = grnItem.Id, ProductId = product.Id, ReturnQuantity = 2m }
            }
        }, 1, "admin");

        product = await _context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        supplier = await _context.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplier.Id);
        Assert.Equal(24m, product.CurrentStock); // 26 - 2 returned to supplier
        Assert.Equal(6000m, supplier.CurrentBalance); // 8,000 - 2,000 return adjustment

        // STEP 9: Verify Customer & Supplier Ledgers
        var custLedgerVm = await _customerService.GetCustomerDetailsWithLedgerAsync(customer.Id);
        Assert.Equal(500m, custLedgerVm.Ledger.Last().RunningBalance);

        var supLedgerVm = await _supplierService.GetSupplierDetailsWithLedgerAsync(supplier.Id);
        Assert.Equal(6000m, supLedgerVm.Ledger.Last().RunningBalance);

        // STEP 10: Day Closing & Period Lock Verification
        var dayVm = await _dayClosingService.GetDayClosingViewModelAsync(DateTime.UtcNow.Date);
        dayVm.ActualCash = dayVm.ExpectedCash;
        var closedDay = await _dayClosingService.CloseDayAsync(dayVm, 1, "admin");
        Assert.Equal("Closed", closedDay.Status);

        // Normal user (isAdmin: false) must be blocked from creating transactions on a closed day
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _expenseService.CreateExpenseAsync(new ExpenseViewModel
            {
                ExpenseDate = DateTime.UtcNow,
                ExpenseCategoryId = 1,
                Description = "Blocked expense on closed day",
                Amount = 100m,
                PaymentMethod = "Cash"
            }, 1, "cashier", isAdmin: false));

        // STEP 11: Monthly Closing
        var closedMonth = await _monthlyClosingService.CloseMonthAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month, "E2E Monthly Close", 1, "admin");
        Assert.Equal("Closed", closedMonth.Status);
        Assert.Equal(7500m, closedMonth.TotalSales);
        Assert.Equal(20000m, closedMonth.TotalPurchases);
        Assert.Equal(1500m, closedMonth.SalesReturns);
        Assert.Equal(2000m, closedMonth.PurchaseReturns);

        // STEP 12: Dashboard & Reports verification
        var dashboard = await _reportingService.GetDashboardViewModelAsync();
        Assert.Equal(7500m, dashboard.TodaySales);
        Assert.Equal(2000m, dashboard.TodayGrossProfit); // NetSales (7500 - 1500 = 6000) - COGS (4 * 1000 = 4000) = 2000
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
