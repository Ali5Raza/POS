using Microsoft.EntityFrameworkCore;
using POSSystem.Helpers;
using POSSystem.Models.Entities;

namespace POSSystem.Data.Seed;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync();

        // 1. Seed Warehouse
        if (!await context.Warehouses.AnyAsync())
        {
            context.Warehouses.Add(new Warehouse
            {
                Code = "MAIN",
                Name = "Main Retail Store & Warehouse",
                Address = "Plot 42, Blue Area Commercial Hub, Islamabad",
                IsDefault = true,
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        // 2. Seed Permissions
        var allPermissions = GetDefaultPermissions();
        var existingPermCodes = await context.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var p in allPermissions)
        {
            if (!existingPermCodes.Contains(p.Code))
            {
                context.Permissions.Add(p);
            }
        }
        await context.SaveChangesAsync();

        // 3. Seed Roles & RolePermissions
        if (!await context.Roles.AnyAsync())
        {
            var permissions = await context.Permissions.ToListAsync();

            var adminRole = new Role { Name = "Admin", Description = "Full system administrator access", IsSystemRole = true };
            var managerRole = new Role { Name = "Manager", Description = "Store manager with operational & supervisor permissions", IsSystemRole = true };
            var cashierRole = new Role { Name = "Cashier", Description = "POS cashier for retail sales, customer receipts & returns", IsSystemRole = true };
            var invManagerRole = new Role { Name = "Inventory Manager", Description = "Catalog, purchasing, goods receiving & inventory control", IsSystemRole = true };
            var accountantRole = new Role { Name = "Accountant", Description = "Financial payments, expenses, reports & period closings", IsSystemRole = true };

            context.Roles.AddRange(adminRole, managerRole, cashierRole, invManagerRole, accountantRole);
            await context.SaveChangesAsync();

            // Admin gets all permissions
            foreach (var perm in permissions)
            {
                context.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = perm.Id });
            }

            // Manager gets everything except ManageRoles
            var managerCodes = permissions.Where(p => p.Code != PermissionCodes.ManageRoles).ToList();
            foreach (var perm in managerCodes)
            {
                context.RolePermissions.Add(new RolePermission { RoleId = managerRole.Id, PermissionId = perm.Id });
            }

            // Cashier permissions
            var cashierCodes = new[]
            {
                PermissionCodes.ViewProducts,
                PermissionCodes.ViewSales,
                PermissionCodes.CreateSale,
                PermissionCodes.SalesReturn,
                PermissionCodes.ViewPayments,
                PermissionCodes.CreatePayment
            };
            foreach (var perm in permissions.Where(p => cashierCodes.Contains(p.Code)))
            {
                context.RolePermissions.Add(new RolePermission { RoleId = cashierRole.Id, PermissionId = perm.Id });
            }

            // Inventory Manager permissions
            var invCodes = new[]
            {
                PermissionCodes.ViewProducts, PermissionCodes.CreateProduct, PermissionCodes.EditProduct,
                PermissionCodes.ViewPurchases, PermissionCodes.CreatePurchase, PermissionCodes.ApprovePurchase,
                PermissionCodes.ReceiveGoods, PermissionCodes.PurchaseReturn,
                PermissionCodes.ViewInventory, PermissionCodes.AdjustInventory,
                PermissionCodes.ViewReports
            };
            foreach (var perm in permissions.Where(p => invCodes.Contains(p.Code)))
            {
                context.RolePermissions.Add(new RolePermission { RoleId = invManagerRole.Id, PermissionId = perm.Id });
            }

            // Accountant permissions
            var accCodes = new[]
            {
                PermissionCodes.ViewProducts, PermissionCodes.ViewPurchases, PermissionCodes.ViewSales,
                PermissionCodes.ViewInventory, PermissionCodes.ViewPayments, PermissionCodes.CreatePayment,
                PermissionCodes.ViewExpenses, PermissionCodes.CreateExpense,
                PermissionCodes.ViewReports, PermissionCodes.ExportReports,
                PermissionCodes.DayClosing, PermissionCodes.MonthlyClosing, PermissionCodes.ViewAuditLogs
            };
            foreach (var perm in permissions.Where(p => accCodes.Contains(p.Code)))
            {
                context.RolePermissions.Add(new RolePermission { RoleId = accountantRole.Id, PermissionId = perm.Id });
            }

            await context.SaveChangesAsync();
        }

        // 4. Seed Default Users
        if (!await context.Users.AnyAsync())
        {
            var initialAdminPassword = Environment.GetEnvironmentVariable("POS_INITIAL_ADMIN_PASSWORD")
                ?? configuration["SeedSettings:InitialAdminPassword"]
                ?? "Admin@123";
            var initialStaffPassword = configuration["SeedSettings:InitialStaffPassword"] ?? "Staff@123";

            var roles = await context.Roles.ToDictionaryAsync(r => r.Name, r => r.Id);

            var adminUser = new ApplicationUser
            {
                Username = "admin",
                FullName = "System Administrator",
                Email = "admin@apexretailpos.com",
                RoleId = roles["Admin"],
                IsActive = true
            };
            adminUser.PasswordHash = PasswordHelper.HashPassword(adminUser, initialAdminPassword);

            var managerUser = new ApplicationUser
            {
                Username = "manager",
                FullName = "Usman Tariq (Store Manager)",
                Email = "manager@apexretailpos.com",
                RoleId = roles["Manager"],
                IsActive = true
            };
            managerUser.PasswordHash = PasswordHelper.HashPassword(managerUser, initialStaffPassword);

            var cashierUser = new ApplicationUser
            {
                Username = "cashier",
                FullName = "Sara Khan (POS Cashier)",
                Email = "cashier@apexretailpos.com",
                RoleId = roles["Cashier"],
                IsActive = true
            };
            cashierUser.PasswordHash = PasswordHelper.HashPassword(cashierUser, initialStaffPassword);

            var invUser = new ApplicationUser
            {
                Username = "inventory",
                FullName = "Kamran Ali (Inventory Manager)",
                Email = "inventory@apexretailpos.com",
                RoleId = roles["Inventory Manager"],
                IsActive = true
            };
            invUser.PasswordHash = PasswordHelper.HashPassword(invUser, initialStaffPassword);

            var accUser = new ApplicationUser
            {
                Username = "accountant",
                FullName = "Zainab Raza (Senior Accountant)",
                Email = "accountant@apexretailpos.com",
                RoleId = roles["Accountant"],
                IsActive = true
            };
            accUser.PasswordHash = PasswordHelper.HashPassword(accUser, initialStaffPassword);

            context.Users.AddRange(adminUser, managerUser, cashierUser, invUser, accUser);
            await context.SaveChangesAsync();
        }

        // 5. Seed System Settings
        if (!await context.SystemSettings.AnyAsync())
        {
            var settings = new List<SystemSetting>
            {
                // Business
                new() { Key = "BusinessName", Value = "Apex Retail Mart & Superstore", Group = "Business", Description = "Registered store name on receipts and reports" },
                new() { Key = "BusinessAddress", Value = "Plot 42, Blue Area Commercial Hub, Islamabad", Group = "Business", Description = "Physical store address" },
                new() { Key = "BusinessPhone", Value = "+92-51-8901234", Group = "Business", Description = "Customer support phone number" },
                new() { Key = "BusinessEmail", Value = "support@apexretailpos.com", Group = "Business", Description = "Store email address" },
                new() { Key = "BusinessLogo", Value = "/images/logo.svg", Group = "Business", Description = "Logo path for invoices and receipts" },
                new() { Key = "TaxNumber", Value = "NTN-4829104-7", Group = "Business", Description = "Business Tax / NTN Registration Number" },
                new() { Key = "InvoiceFooter", Value = "Thank you for shopping with us! Goods once sold can be exchanged within 7 days with original receipt.", Group = "Business", Description = "Footer message printed on thermal & A4 invoices" },

                // POS
                new() { Key = "DefaultCustomer", Value = "Walk-in Customer", Group = "POS", Description = "Default customer for POS terminal" },
                new() { Key = "DefaultPaymentMethod", Value = "Cash", Group = "POS", Description = "Default checkout payment method" },
                new() { Key = "AutoPrint", Value = "false", Group = "POS", Description = "Automatically open print dialog after completing sale" },
                new() { Key = "ReceiptFormat", Value = "Thermal", Group = "POS", Description = "Default receipt format (Thermal or A4)" },
                new() { Key = "AllowNegativeStock", Value = "false", Group = "POS", Description = "Allow selling products when stock is zero or insufficient" },
                new() { Key = "MaximumDiscountPercent", Value = "20", Group = "POS", Description = "Max discount % allowed for non-supervisor users" },
                new() { Key = "RequireCustomerForCreditSale", Value = "true", Group = "POS", Description = "Require registered customer selection for credit sales" },

                // Inventory
                new() { Key = "DefaultMinimumStock", Value = "5", Group = "Inventory", Description = "Default minimum stock alert level for new products" },
                new() { Key = "DefaultReorderLevel", Value = "15", Group = "Inventory", Description = "Default reorder threshold for new products" },
                new() { Key = "NegativeStockPolicy", Value = "Block", Group = "Inventory", Description = "Negative stock policy (Block or Warn)" },

                // Tax
                new() { Key = "EnableTax", Value = "true", Group = "Tax", Description = "Enable tax calculation on sales and purchases" },
                new() { Key = "DefaultTaxRate", Value = "5.0", Group = "Tax", Description = "Default tax rate percentage" },

                // Numbering
                new() { Key = "Prefix_INV", Value = "INV", Group = "Numbering", Description = "Sales Invoice prefix" },
                new() { Key = "Prefix_PO", Value = "PO", Group = "Numbering", Description = "Purchase Order prefix" },
                new() { Key = "Prefix_GRN", Value = "GRN", Group = "Numbering", Description = "Goods Receipt Note prefix" },
                new() { Key = "Prefix_PR", Value = "PR", Group = "Numbering", Description = "Purchase Return prefix" },
                new() { Key = "Prefix_SR", Value = "SR", Group = "Numbering", Description = "Sales Return prefix" },
                new() { Key = "Prefix_PAY", Value = "PAY", Group = "Numbering", Description = "Payment voucher prefix" },
                new() { Key = "Prefix_EXP", Value = "EXP", Group = "Numbering", Description = "Expense voucher prefix" }
            };
            context.SystemSettings.AddRange(settings);
            await context.SaveChangesAsync();
        }

        // 6. Seed Expense Categories
        if (!await context.ExpenseCategories.AnyAsync())
        {
            var expenseCategories = new List<ExpenseCategory>
            {
                new() { Name = "Rent", Description = "Monthly store and warehouse premises rent", IsActive = true },
                new() { Name = "Electricity", Description = "Utility & power bills", IsActive = true },
                new() { Name = "Salary", Description = "Staff wages, salaries and bonuses", IsActive = true },
                new() { Name = "Transport", Description = "Delivery fuel, freight and logistics", IsActive = true },
                new() { Name = "Internet", Description = "Broadband, phone and communication services", IsActive = true },
                new() { Name = "Maintenance", Description = "Equipment, AC, electrical and store repairs", IsActive = true },
                new() { Name = "Office", Description = "Thermal paper rolls, stationery and office supplies", IsActive = true },
                new() { Name = "Miscellaneous", Description = "General store refreshments and sundry expenses", IsActive = true }
            };
            context.ExpenseCategories.AddRange(expenseCategories);
            await context.SaveChangesAsync();
        }

        // 7. Seed Product Categories (including nested categories)
        if (!await context.ProductCategories.AnyAsync())
        {
            var beverages = new ProductCategory { Name = "Beverages & Drinks", Description = "Juices, soft drinks, mineral water, tea & coffee", IsActive = true };
            var groceries = new ProductCategory { Name = "Groceries & Staples", Description = "Cooking oil, rice, flour, spices & pulses", IsActive = true };
            var dairy = new ProductCategory { Name = "Dairy & Bakery", Description = "Milk, butter, cheese, bread, eggs & cereals", IsActive = true };
            var snacks = new ProductCategory { Name = "Snacks & Confectionery", Description = "Biscuits, chips, chocolates & nuts", IsActive = true };
            var personalCare = new ProductCategory { Name = "Personal Care & Hygiene", Description = "Soaps, shampoos, skincare & oral care", IsActive = true };
            var household = new ProductCategory { Name = "Household & Cleaning", Description = "Detergents, dishwashers, tissues & cleaners", IsActive = true };
            var electronics = new ProductCategory { Name = "Electronics & Accessories", Description = "Batteries, LED bulbs, charging cables & gadgets", IsActive = true };

            context.ProductCategories.AddRange(beverages, groceries, dairy, snacks, personalCare, household, electronics);
            await context.SaveChangesAsync();

            var coffeeTea = new ProductCategory
            {
                Name = "Coffee & Tea",
                Description = "Instant coffee, green tea & black tea packs",
                ParentCategoryId = beverages.Id,
                IsActive = true
            };
            context.ProductCategories.Add(coffeeTea);
            await context.SaveChangesAsync();
        }

        // 8. Seed Suppliers
        if (!await context.Suppliers.AnyAsync())
        {
            var suppliers = new List<Supplier>
            {
                new()
                {
                    Name = "Salman Qureshi",
                    CompanyName = "Metro Cash & Carry Distributors",
                    Phone = "+92-300-5112233",
                    Email = "orders@metrodistributors.pk",
                    Address = "I-9 Industrial Area, Street 4, Islamabad",
                    TaxNumber = "NTN-1122334-8",
                    OpeningBalance = 0,
                    CurrentBalance = 18500m,
                    Notes = "Primary wholesale distributor for groceries and household items",
                    IsActive = true
                },
                new()
                {
                    Name = "Haris Mahmood",
                    CompanyName = "Nestle & Beverages Supply Co.",
                    Phone = "+92-321-5448899",
                    Email = "supply@nestlebeverages.pk",
                    Address = "Sector F-11 Markaz, Plaza 12, Islamabad",
                    TaxNumber = "NTN-3399112-1",
                    OpeningBalance = 0,
                    CurrentBalance = 0m,
                    Notes = "Direct distributor for dairy, coffee, water and juices",
                    IsActive = true
                },
                new()
                {
                    Name = "Faisal Sheikh",
                    CompanyName = "Unilever Consumer Goods Ltd.",
                    Phone = "+92-333-5776655",
                    Email = "b2b@unilevergoods.pk",
                    Address = "Rawat Industrial Estate, GT Road",
                    TaxNumber = "NTN-7788990-4",
                    OpeningBalance = 5000m,
                    CurrentBalance = 5000m,
                    Notes = "Personal care, hygiene and cleaning products supplier",
                    IsActive = true
                },
                new()
                {
                    Name = "Nadia Javed",
                    CompanyName = "Prime Tech & Retail Accessories",
                    Phone = "+92-345-8119900",
                    Email = "sales@primetechwholesale.pk",
                    Address = "Blue Area Block C, Islamabad",
                    TaxNumber = "NTN-9911223-5",
                    OpeningBalance = 0,
                    CurrentBalance = 0m,
                    Notes = "Electronics, batteries and lighting products",
                    IsActive = true
                }
            };
            context.Suppliers.AddRange(suppliers);
            await context.SaveChangesAsync();
        }

        // 9. Seed Customers (Walk-in Customer MUST be first / default)
        if (!await context.Customers.AnyAsync())
        {
            var customers = new List<Customer>
            {
                new()
                {
                    Name = "Walk-in Customer",
                    Phone = "000-0000000",
                    Email = "walkin@store.local",
                    Address = "Counter Retail Sale",
                    OpeningBalance = 0,
                    CreditLimit = 0,
                    CurrentBalance = 0,
                    IsWalkIn = true,
                    IsActive = true
                },
                new()
                {
                    Name = "Tariq Mehmood",
                    Phone = "+92-301-5551020",
                    Email = "tariq.mehmood@gmail.com",
                    Address = "House 18, Street 11, F-8/3, Islamabad",
                    OpeningBalance = 0,
                    CreditLimit = 50000m,
                    CurrentBalance = 3200m,
                    IsWalkIn = false,
                    IsActive = true
                },
                new()
                {
                    Name = "Ayesha Siddiqui",
                    Phone = "+92-322-9876543",
                    Email = "ayesha.s@outlook.com",
                    Address = "Apartment 4B, Silver Oaks, F-10, Islamabad",
                    OpeningBalance = 0,
                    CreditLimit = 35000m,
                    CurrentBalance = 0m,
                    IsWalkIn = false,
                    IsActive = true
                },
                new()
                {
                    Name = "Horizon Tech Corporate Office",
                    Phone = "+92-51-2233445",
                    Email = "procurement@horizontech.pk",
                    Address = "Level 6, Evacuee Trust Complex, Islamabad",
                    OpeningBalance = 0,
                    CreditLimit = 150000m,
                    CurrentBalance = 0m,
                    IsWalkIn = false,
                    IsActive = true
                },
                new()
                {
                    Name = "Bilal Ahmed Khan",
                    Phone = "+92-334-1122334",
                    Email = "bilal.khan@yahoo.com",
                    Address = "Street 24, G-11/2, Islamabad",
                    OpeningBalance = 0,
                    CreditLimit = 25000m,
                    CurrentBalance = 0m,
                    IsWalkIn = false,
                    IsActive = true
                }
            };
            context.Customers.AddRange(customers);
            await context.SaveChangesAsync();
        }

        // 10. Seed Products & Opening Stock Movements
        if (!await context.Products.AnyAsync())
        {
            var cats = await context.ProductCategories.ToDictionaryAsync(c => c.Name, c => c.Id);
            var sups = await context.Suppliers.ToListAsync();
            var adminUser = await context.Users.FirstAsync();
            var warehouse = await context.Warehouses.FirstAsync();

            var products = new List<Product>
            {
                new()
                {
                    SKU = "SKU-1001", Barcode = "896100100001",
                    Name = "Nescafe Classic Instant Coffee 200g",
                    Description = "Rich aroma premium glass jar instant coffee",
                    CategoryId = cats.GetValueOrDefault("Coffee & Tea", cats["Beverages & Drinks"]),
                    DefaultSupplierId = sups[1].Id,
                    CostPrice = 950m, SalePrice = 1250m, WholesalePrice = 1150m, MinimumSalePrice = 1080m,
                    TaxRate = 5m, Unit = "jar",
                    OpeningStock = 45, CurrentStock = 45, MinimumStockLevel = 10, ReorderLevel = 20,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1002", Barcode = "896100100002",
                    Name = "Lipton Yellow Label Tea 900g",
                    Description = "Family pack finest black tea blend",
                    CategoryId = cats.GetValueOrDefault("Coffee & Tea", cats["Beverages & Drinks"]),
                    DefaultSupplierId = sups[2].Id,
                    CostPrice = 1180m, SalePrice = 1480m, WholesalePrice = 1390m, MinimumSalePrice = 1300m,
                    TaxRate = 5m, Unit = "pack",
                    OpeningStock = 60, CurrentStock = 60, MinimumStockLevel = 12, ReorderLevel = 25,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1003", Barcode = "896100100003",
                    Name = "Nestle Pure Life Mineral Water 1.5L",
                    Description = "Pure bottled drinking water 1.5 Liter",
                    CategoryId = cats["Beverages & Drinks"],
                    DefaultSupplierId = sups[1].Id,
                    CostPrice = 65m, SalePrice = 95m, WholesalePrice = 85m, MinimumSalePrice = 80m,
                    TaxRate = 5m, Unit = "btl",
                    OpeningStock = 180, CurrentStock = 180, MinimumStockLevel = 30, ReorderLevel = 60,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1004", Barcode = "896100100004",
                    Name = "Guard Super Kernel Basmati Rice 5kg",
                    Description = "Aged extra long grain aromatic basmati rice",
                    CategoryId = cats["Groceries & Staples"],
                    DefaultSupplierId = sups[0].Id,
                    CostPrice = 1550m, SalePrice = 1950m, WholesalePrice = 1820m, MinimumSalePrice = 1750m,
                    TaxRate = 0m, Unit = "bag",
                    OpeningStock = 40, CurrentStock = 40, MinimumStockLevel = 8, ReorderLevel = 15,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1005", Barcode = "896100100005",
                    Name = "Dalda Cooking Oil Pouch 5x1L Carton",
                    Description = "Cholesterol-free fortified cooking oil 5 Liter pack",
                    CategoryId = cats["Groceries & Staples"],
                    DefaultSupplierId = sups[0].Id,
                    CostPrice = 2250m, SalePrice = 2650m, WholesalePrice = 2520m, MinimumSalePrice = 2450m,
                    TaxRate = 0m, Unit = "ctn",
                    OpeningStock = 35, CurrentStock = 35, MinimumStockLevel = 10, ReorderLevel = 18,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1006", Barcode = "896100100006",
                    Name = "Olpers Full Cream Milk 1L Pack",
                    Description = "UHT treated standardized full cream milk",
                    CategoryId = cats["Dairy & Bakery"],
                    DefaultSupplierId = sups[1].Id,
                    CostPrice = 245m, SalePrice = 295m, WholesalePrice = 280m, MinimumSalePrice = 270m,
                    TaxRate = 0m, Unit = "pack",
                    OpeningStock = 120, CurrentStock = 120, MinimumStockLevel = 24, ReorderLevel = 48,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1007", Barcode = "896100100007",
                    Name = "Lurpak Unsalted Butter 200g",
                    Description = "Premium Danish dairy butter block",
                    CategoryId = cats["Dairy & Bakery"],
                    DefaultSupplierId = sups[1].Id,
                    CostPrice = 520m, SalePrice = 680m, WholesalePrice = 630m, MinimumSalePrice = 600m,
                    TaxRate = 5m, Unit = "pcs",
                    OpeningStock = 28, CurrentStock = 28, MinimumStockLevel = 6, ReorderLevel = 12,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1008", Barcode = "896100100008",
                    Name = "LU Oreo Original Family Pack 12x",
                    Description = "Chocolate sandwich cookies with vanilla creme",
                    CategoryId = cats["Snacks & Confectionery"],
                    DefaultSupplierId = sups[0].Id,
                    CostPrice = 340m, SalePrice = 450m, WholesalePrice = 410m, MinimumSalePrice = 390m,
                    TaxRate = 5m, Unit = "box",
                    OpeningStock = 75, CurrentStock = 75, MinimumStockLevel = 15, ReorderLevel = 30,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1009", Barcode = "896100100009",
                    Name = "Lay's Classic Salted Mega Pack 150g",
                    Description = "Crispy potato chips lightly salted",
                    CategoryId = cats["Snacks & Confectionery"],
                    DefaultSupplierId = sups[0].Id,
                    CostPrice = 145m, SalePrice = 200m, WholesalePrice = 180m, MinimumSalePrice = 170m,
                    TaxRate = 5m, Unit = "pack",
                    OpeningStock = 90, CurrentStock = 90, MinimumStockLevel = 20, ReorderLevel = 40,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1010", Barcode = "896100100010",
                    Name = "Dove Daily Moisture Shampoo 400ml",
                    Description = "Nourishing hair care shampoo bottle",
                    CategoryId = cats["Personal Care & Hygiene"],
                    DefaultSupplierId = sups[2].Id,
                    CostPrice = 580m, SalePrice = 780m, WholesalePrice = 720m, MinimumSalePrice = 680m,
                    TaxRate = 5m, Unit = "btl",
                    OpeningStock = 32, CurrentStock = 32, MinimumStockLevel = 8, ReorderLevel = 15,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1011", Barcode = "896100100011",
                    Name = "Sensodyne Rapid Relief Toothpaste 100g",
                    Description = "Clinically proven sensitivity relief toothpaste",
                    CategoryId = cats["Personal Care & Hygiene"],
                    DefaultSupplierId = sups[2].Id,
                    CostPrice = 390m, SalePrice = 520m, WholesalePrice = 480m, MinimumSalePrice = 450m,
                    TaxRate = 5m, Unit = "pcs",
                    OpeningStock = 4, CurrentStock = 4, MinimumStockLevel = 8, ReorderLevel = 16, // Low Stock sample
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1012", Barcode = "896100100012",
                    Name = "Surf Excel Matic Detergent Powder 2kg",
                    Description = "Front and top load washing machine detergent",
                    CategoryId = cats["Household & Cleaning"],
                    DefaultSupplierId = sups[2].Id,
                    CostPrice = 920m, SalePrice = 1190m, WholesalePrice = 1100m, MinimumSalePrice = 1050m,
                    TaxRate = 5m, Unit = "bag",
                    OpeningStock = 50, CurrentStock = 50, MinimumStockLevel = 10, ReorderLevel = 20,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1013", Barcode = "896100100013",
                    Name = "Lemon Max Dishwash Liquid 750ml",
                    Description = "Concentrated grease-cutting dishwashing gel",
                    CategoryId = cats["Household & Cleaning"],
                    DefaultSupplierId = sups[2].Id,
                    CostPrice = 290m, SalePrice = 390m, WholesalePrice = 360m, MinimumSalePrice = 340m,
                    TaxRate = 5m, Unit = "btl",
                    OpeningStock = 3, CurrentStock = 3, MinimumStockLevel = 6, ReorderLevel = 12, // Low Stock sample
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1014", Barcode = "896100100014",
                    Name = "Philips LED Bulb 12W Cool Daylight",
                    Description = "Energy saving E27 screw base LED bulb",
                    CategoryId = cats["Electronics & Accessories"],
                    DefaultSupplierId = sups[3].Id,
                    CostPrice = 310m, SalePrice = 450m, WholesalePrice = 410m, MinimumSalePrice = 380m,
                    TaxRate = 5m, Unit = "pcs",
                    OpeningStock = 42, CurrentStock = 42, MinimumStockLevel = 10, ReorderLevel = 20,
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1015", Barcode = "896100100015",
                    Name = "Energizer Max AA Alkaline Batteries 4-Pack",
                    Description = "Long-lasting AA power batteries pack of 4",
                    CategoryId = cats["Electronics & Accessories"],
                    DefaultSupplierId = sups[3].Id,
                    CostPrice = 420m, SalePrice = 590m, WholesalePrice = 540m, MinimumSalePrice = 500m,
                    TaxRate = 5m, Unit = "pack",
                    OpeningStock = 0, CurrentStock = 0, MinimumStockLevel = 10, ReorderLevel = 20, // Out of Stock sample
                    IsActive = true
                },
                new()
                {
                    SKU = "SKU-1016", Barcode = "896100100016",
                    Name = "Anker Braided USB-C Fast Charging Cable 1.8m",
                    Description = "60W PowerLine III durable braided Type-C cable",
                    CategoryId = cats["Electronics & Accessories"],
                    DefaultSupplierId = sups[3].Id,
                    CostPrice = 750m, SalePrice = 1100m, WholesalePrice = 980m, MinimumSalePrice = 900m,
                    TaxRate = 5m, Unit = "pcs",
                    OpeningStock = 25, CurrentStock = 25, MinimumStockLevel = 5, ReorderLevel = 10,
                    IsActive = true
                }
            };

            context.Products.AddRange(products);
            await context.SaveChangesAsync();

            foreach (var prod in products.Where(p => p.OpeningStock > 0))
            {
                context.StockMovements.Add(new StockMovement
                {
                    ProductId = prod.Id,
                    WarehouseId = warehouse.Id,
                    TransactionType = "Opening Stock",
                    ReferenceNumber = $"OPEN-{prod.SKU}",
                    QuantityIn = prod.OpeningStock,
                    QuantityOut = 0,
                    BalanceAfter = prod.OpeningStock,
                    UnitCost = prod.CostPrice,
                    Notes = "Initial opening inventory balance",
                    UserId = adminUser.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                });
            }
            await context.SaveChangesAsync();
        }

        // 11. Seed Sample Transactions (PurchaseOrder, GRN, Sales, Payments, Expenses, AuditLog)
        if (!await context.Sales.AnyAsync())
        {
            var adminUser = await context.Users.FirstAsync(u => u.Username == "admin");
            var cashierUser = await context.Users.FirstAsync(u => u.Username == "cashier");
            var walkInCustomer = await context.Customers.FirstAsync(c => c.IsWalkIn);
            var regCustomer = await context.Customers.FirstAsync(c => !c.IsWalkIn);
            var supplier = await context.Suppliers.FirstAsync();
            var warehouse = await context.Warehouses.FirstAsync();
            var products = await context.Products.ToListAsync();
            var expCat = await context.ExpenseCategories.FirstAsync(e => e.Name == "Electricity");

            // Sample Purchase Order & GRN
            var po = new PurchaseOrder
            {
                PONumber = "PO-202610-0001",
                SupplierId = supplier.Id,
                OrderDate = DateTime.UtcNow.AddDays(-3),
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(-2),
                SubTotal = 38500m,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                TotalAmount = 38500m,
                Status = "FullyReceived",
                Notes = "Weekly restock order",
                CreatedByUserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            };
            var poItem = new PurchaseOrderItem
            {
                ProductId = products[3].Id, // Basmati Rice
                OrderedQuantity = 10,
                ReceivedQuantity = 10,
                CostPrice = 1550m,
                LineTotal = 15500m
            };
            var poItem2 = new PurchaseOrderItem
            {
                ProductId = products[4].Id, // Cooking Oil
                OrderedQuantity = 10,
                ReceivedQuantity = 10,
                CostPrice = 2300m,
                LineTotal = 23000m
            };
            po.Items.Add(poItem);
            po.Items.Add(poItem2);
            context.PurchaseOrders.Add(po);
            await context.SaveChangesAsync();

            var grn = new GoodsReceipt
            {
                GRNNumber = "GRN-202610-0001",
                SupplierId = supplier.Id,
                PurchaseOrderId = po.Id,
                WarehouseId = warehouse.Id,
                ReceivingDate = DateTime.UtcNow.AddDays(-2),
                SupplierInvoiceNumber = "SUP-INV-8821",
                SubTotal = 38500m,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                TotalAmount = 38500m,
                PaidAmount = 20000m,
                DueAmount = 18500m,
                PaymentMethod = "Bank Transfer",
                Status = "Completed",
                Notes = "Received in good condition",
                CreatedByUserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            };
            grn.Items.Add(new GoodsReceiptItem
            {
                PurchaseOrderItemId = poItem.Id,
                ProductId = products[3].Id,
                OrderedQuantity = 10,
                ReceivedQuantity = 10,
                DamagedQuantity = 0,
                CostPrice = 1550m,
                LineTotal = 15500m
            });
            grn.Items.Add(new GoodsReceiptItem
            {
                PurchaseOrderItemId = poItem2.Id,
                ProductId = products[4].Id,
                OrderedQuantity = 10,
                ReceivedQuantity = 10,
                DamagedQuantity = 0,
                CostPrice = 2300m,
                LineTotal = 23000m
            });
            context.GoodsReceipts.Add(grn);

            // Increase stock for GRN items
            products[3].CurrentStock += 10;
            products[4].CurrentStock += 10;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[3].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Purchase",
                ReferenceNumber = grn.GRNNumber,
                QuantityIn = 10,
                QuantityOut = 0,
                BalanceAfter = products[3].CurrentStock,
                UnitCost = 1550m,
                Notes = "Goods Received via GRN-202610-0001",
                UserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });
            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[4].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Purchase",
                ReferenceNumber = grn.GRNNumber,
                QuantityIn = 10,
                QuantityOut = 0,
                BalanceAfter = products[4].CurrentStock,
                UnitCost = 2300m,
                Notes = "Goods Received via GRN-202610-0001",
                UserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });

            await context.SaveChangesAsync();

            context.Payments.Add(new Payment
            {
                PaymentNumber = "PAY-202610-0001",
                PaymentDate = DateTime.UtcNow.AddDays(-2),
                PaymentType = "PurchasePayment",
                Amount = 20000m,
                PaymentMethod = "Bank Transfer",
                ReferenceNumber = "TXN-994021",
                SupplierId = supplier.Id,
                GoodsReceiptId = grn.Id,
                Notes = "Partial payment against GRN-202610-0001",
                UserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });

            // Sample Sale 1 (Yesterday - Cash Walk-in)
            var sale1 = new Sale
            {
                InvoiceNumber = "INV-202610-0001",
                SaleDate = DateTime.UtcNow.AddDays(-1),
                CustomerId = walkInCustomer.Id,
                IsWalkIn = true,
                CashierId = cashierUser.Id,
                WarehouseId = warehouse.Id,
                PriceTier = "Retail",
                SubTotal = 3980m,
                DiscountAmount = 80m,
                TaxAmount = 195m,
                TotalAmount = 4095m,
                PaidAmount = 4500m,
                ChangeAmount = 405m,
                DueAmount = 0m,
                PaymentMethod = "Cash",
                Status = "Completed",
                Notes = "Counter retail sale",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            };
            sale1.Items.Add(new SaleItem
            {
                ProductId = products[0].Id, // Nescafe 1250 x 2 = 2500
                Quantity = 2,
                UnitCost = products[0].CostPrice,
                UnitPrice = 1250m,
                OriginalUnitPrice = 1250m,
                DiscountAmount = 50m,
                TaxRate = 5m,
                TaxAmount = 122.50m,
                LineTotal = 2572.50m
            });
            sale1.Items.Add(new SaleItem
            {
                ProductId = products[1].Id, // Lipton 1480 x 1 = 1480
                Quantity = 1,
                UnitCost = products[1].CostPrice,
                UnitPrice = 1480m,
                OriginalUnitPrice = 1480m,
                DiscountAmount = 30m,
                TaxRate = 5m,
                TaxAmount = 72.50m,
                LineTotal = 1522.50m
            });
            context.Sales.Add(sale1);

            products[0].CurrentStock -= 2;
            products[1].CurrentStock -= 1;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[0].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Sale",
                ReferenceNumber = sale1.InvoiceNumber,
                QuantityIn = 0,
                QuantityOut = 2,
                BalanceAfter = products[0].CurrentStock,
                UnitCost = products[0].CostPrice,
                Notes = "POS Retail Sale",
                UserId = cashierUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[1].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Sale",
                ReferenceNumber = sale1.InvoiceNumber,
                QuantityIn = 0,
                QuantityOut = 1,
                BalanceAfter = products[1].CurrentStock,
                UnitCost = products[1].CostPrice,
                Notes = "POS Retail Sale",
                UserId = cashierUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

            await context.SaveChangesAsync();

            context.Payments.Add(new Payment
            {
                PaymentNumber = "PAY-202610-0002",
                PaymentDate = DateTime.UtcNow.AddDays(-1),
                PaymentType = "SaleReceipt",
                Amount = 4095m,
                PaymentMethod = "Cash",
                ReferenceNumber = sale1.InvoiceNumber,
                CustomerId = walkInCustomer.Id,
                SaleId = sale1.Id,
                Notes = "POS Cash Receipt",
                UserId = cashierUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

            // Sample Sale 2 (Today - Partial Credit Sale to Registered Customer)
            var today10Am = DateTime.UtcNow.Date.AddHours(10);
            var sale2 = new Sale
            {
                InvoiceNumber = "INV-202610-0002",
                SaleDate = today10Am,
                CustomerId = regCustomer.Id,
                IsWalkIn = false,
                CashierId = cashierUser.Id,
                WarehouseId = warehouse.Id,
                PriceTier = "Retail",
                SubTotal = 5200m,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                TotalAmount = 5200m,
                PaidAmount = 2000m,
                ChangeAmount = 0m,
                DueAmount = 3200m,
                PaymentMethod = "Cash",
                Status = "Completed",
                Notes = "Monthly household grocery order",
                CreatedAt = today10Am
            };
            sale2.Items.Add(new SaleItem
            {
                ProductId = products[3].Id, // Basmati Rice 1950 x 2 = 3900
                Quantity = 2,
                UnitCost = products[3].CostPrice,
                UnitPrice = 1950m,
                OriginalUnitPrice = 1950m,
                DiscountAmount = 0m,
                TaxRate = 0m,
                TaxAmount = 0m,
                LineTotal = 3900m
            });
            sale2.Items.Add(new SaleItem
            {
                ProductId = products[5].Id, // Olpers Milk 295 -> 4 packs + Oreo
                Quantity = 4,
                UnitCost = products[5].CostPrice,
                UnitPrice = 325m,
                OriginalUnitPrice = 325m,
                DiscountAmount = 0m,
                TaxRate = 0m,
                TaxAmount = 0m,
                LineTotal = 1300m
            });
            context.Sales.Add(sale2);

            products[3].CurrentStock -= 2;
            products[5].CurrentStock -= 4;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[3].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Sale",
                ReferenceNumber = sale2.InvoiceNumber,
                QuantityIn = 0,
                QuantityOut = 2,
                BalanceAfter = products[3].CurrentStock,
                UnitCost = products[3].CostPrice,
                Notes = "POS Sale INV-202610-0002",
                UserId = cashierUser.Id,
                CreatedAt = today10Am
            });
            context.StockMovements.Add(new StockMovement
            {
                ProductId = products[5].Id,
                WarehouseId = warehouse.Id,
                TransactionType = "Sale",
                ReferenceNumber = sale2.InvoiceNumber,
                QuantityIn = 0,
                QuantityOut = 4,
                BalanceAfter = products[5].CurrentStock,
                UnitCost = products[5].CostPrice,
                Notes = "POS Sale INV-202610-0002",
                UserId = cashierUser.Id,
                CreatedAt = today10Am
            });

            await context.SaveChangesAsync();

            context.Payments.Add(new Payment
            {
                PaymentNumber = "PAY-202610-0003",
                PaymentDate = today10Am,
                PaymentType = "SaleReceipt",
                Amount = 2000m,
                PaymentMethod = "Cash",
                ReferenceNumber = sale2.InvoiceNumber,
                CustomerId = regCustomer.Id,
                SaleId = sale2.Id,
                Notes = "Partial payment on invoice INV-202610-0002",
                UserId = cashierUser.Id,
                CreatedAt = today10Am
            });

            // Sample Expense Today
            context.Expenses.Add(new Expense
            {
                ExpenseNumber = "EXP-202610-0001",
                ExpenseDate = today10Am,
                ExpenseCategoryId = expCat.Id,
                Description = "Store backup generator fuel & electricity top-up",
                Amount = 1450m,
                PaymentMethod = "Cash",
                ReferenceNumber = "BILL-8812",
                Notes = "Paid from counter petty cash",
                UserId = adminUser.Id,
                CreatedAt = today10Am
            });

            // Sample AuditLog
            context.AuditLogs.Add(new AuditLog
            {
                UserId = adminUser.Id,
                Username = adminUser.Username,
                Action = "SystemSeed",
                EntityName = "Database",
                EntityId = "1",
                NewValue = "Initialized default retail catalog, roles, permissions and initial ledger",
                Timestamp = DateTime.UtcNow,
                IpAddress = "127.0.0.1"
            });

            await context.SaveChangesAsync();
        }

        // Keep seeded sample transactions aligned with the current system date so Today's Dashboard KPIs are always live
        var currentDay = DateTime.UtcNow.Date;
        var currentDayEnd = currentDay.AddDays(1);
        if (!await context.Sales.AnyAsync(s => s.SaleDate >= currentDay && s.SaleDate < currentDayEnd))
        {
            var seedSale2 = await context.Sales.FirstOrDefaultAsync(s => s.InvoiceNumber == "INV-202610-0002");
            var seedSale1 = await context.Sales.FirstOrDefaultAsync(s => s.InvoiceNumber == "INV-202610-0001");
            var seedGrn = await context.GoodsReceipts.FirstOrDefaultAsync(g => g.GRNNumber == "GRN-202610-0001");
            var seedExp = await context.Expenses.FirstOrDefaultAsync(e => e.ExpenseNumber == "EXP-202610-0001");

            if (seedSale2 != null)
            {
                seedSale2.SaleDate = currentDay.AddHours(11);
                seedSale2.CreatedAt = currentDay.AddHours(11);
            }
            if (seedSale1 != null)
            {
                seedSale1.SaleDate = currentDay.AddHours(9);
                seedSale1.CreatedAt = currentDay.AddHours(9);
            }
            if (seedGrn != null)
            {
                seedGrn.ReceivingDate = currentDay.AddHours(8);
                seedGrn.CreatedAt = currentDay.AddHours(8);
            }
            if (seedExp != null)
            {
                seedExp.ExpenseDate = currentDay.AddHours(10);
                seedExp.CreatedAt = currentDay.AddHours(10);
            }
            await context.SaveChangesAsync();
        }
    }

    private static List<Permission> GetDefaultPermissions() => new()
    {
        new() { Code = PermissionCodes.ViewProducts, Name = "View Products & Categories", Module = "Catalog", Description = "View product catalog, pricing and categories" },
        new() { Code = PermissionCodes.CreateProduct, Name = "Create Products & Categories", Module = "Catalog", Description = "Add new products and categories" },
        new() { Code = PermissionCodes.EditProduct, Name = "Edit Products & Categories", Module = "Catalog", Description = "Update product details, pricing and status" },
        new() { Code = PermissionCodes.DeleteProduct, Name = "Deactivate Products", Module = "Catalog", Description = "Deactivate or archive catalog items" },

        new() { Code = PermissionCodes.ViewPurchases, Name = "View Purchases & Suppliers", Module = "Purchasing", Description = "View suppliers, purchase orders and GRNs" },
        new() { Code = PermissionCodes.CreatePurchase, Name = "Create Purchase Orders", Module = "Purchasing", Description = "Create and manage suppliers and purchase orders" },
        new() { Code = PermissionCodes.ApprovePurchase, Name = "Approve Purchase Orders", Module = "Purchasing", Description = "Approve draft purchase orders" },
        new() { Code = PermissionCodes.ReceiveGoods, Name = "Receive Goods (GRN)", Module = "Purchasing", Description = "Process Goods Receipt Notes and increase stock" },
        new() { Code = PermissionCodes.PurchaseReturn, Name = "Process Purchase Returns", Module = "Purchasing", Description = "Return received goods to suppliers" },

        new() { Code = PermissionCodes.ViewSales, Name = "View Sales & Customers", Module = "Sales & POS", Description = "View sales invoices, customers and receipts" },
        new() { Code = PermissionCodes.CreateSale, Name = "Create Sales (POS Terminal)", Module = "Sales & POS", Description = "Operate POS terminal and create retail invoices" },
        new() { Code = PermissionCodes.CancelSale, Name = "Cancel / Void Sales", Module = "Sales & POS", Description = "Cancel or void completed sales invoices" },
        new() { Code = PermissionCodes.SalesReturn, Name = "Process Sales Returns", Module = "Sales & POS", Description = "Accept customer returns and issue refunds" },

        new() { Code = PermissionCodes.ApplyDiscount, Name = "Apply High Discounts", Module = "Sales & POS", Description = "Apply discounts above standard threshold" },
        new() { Code = PermissionCodes.OverridePrice, Name = "Override Unit Prices", Module = "Sales & POS", Description = "Override product sale price or sell below minimum price" },

        new() { Code = PermissionCodes.ViewInventory, Name = "View Inventory & Stock Ledger", Module = "Inventory", Description = "View stock balances, valuation and ledger movements" },
        new() { Code = PermissionCodes.AdjustInventory, Name = "Adjust Inventory Stock", Module = "Inventory", Description = "Perform manual stock increase or decrease adjustments" },

        new() { Code = PermissionCodes.ViewPayments, Name = "View Payments & Ledgers", Module = "Finance", Description = "View customer and supplier payments" },
        new() { Code = PermissionCodes.CreatePayment, Name = "Record Payments", Module = "Finance", Description = "Record customer receipts, supplier payments and refunds" },

        new() { Code = PermissionCodes.ViewExpenses, Name = "View Expenses", Module = "Finance", Description = "View store operating expenses" },
        new() { Code = PermissionCodes.CreateExpense, Name = "Record Expenses", Module = "Finance", Description = "Create expenses and manage expense categories" },

        new() { Code = PermissionCodes.ViewReports, Name = "View Business Reports", Module = "Reporting", Description = "Access sales, purchase, inventory and financial reports" },
        new() { Code = PermissionCodes.ExportReports, Name = "Export Reports (CSV/Excel/Print)", Module = "Reporting", Description = "Export reports to CSV/Excel and print" },

        new() { Code = PermissionCodes.ManageUsers, Name = "Manage Users", Module = "Administration", Description = "Create, edit and activate/deactivate user accounts" },
        new() { Code = PermissionCodes.ManageRoles, Name = "Manage Roles & Permissions", Module = "Administration", Description = "Configure roles and assign granular permissions" },
        new() { Code = PermissionCodes.ManageSettings, Name = "Manage System Settings", Module = "Administration", Description = "Configure business, POS, tax, inventory and numbering settings" },

        new() { Code = PermissionCodes.DayClosing, Name = "Perform Day Closing", Module = "Closing", Description = "Close daily register, reconcile cash and reopen closed days" },
        new() { Code = PermissionCodes.MonthlyClosing, Name = "Perform Monthly Closing", Module = "Closing", Description = "Finalize monthly accounting periods and reopen closed months" },

        new() { Code = PermissionCodes.ViewAuditLogs, Name = "View Audit Logs", Module = "Administration", Description = "Inspect security and operational audit trail" }
    };
}
