# Retail Point of Sale (POS), Inventory, Purchasing & Business Management System (.NET 8 MVC + SQLite)

A complete, production-ready, enterprise-grade **Retail Point of Sale (POS), Inventory, Purchasing, Sales, Payments, Expenses, Day/Monthly Closing, and Business Management System** built as a single deployable **ASP.NET Core .NET 8 MVC** web application using **Razor Views (`.cshtml`)**, **Entity Framework Core 8 Code-First**, and **SQLite (`pos_system.db`)**.

---

## Default Login Credentials

| Role | Username | Password | Description |
| :--- | :--- | :--- | :--- |
| **Administrator** | `admin` | `Admin@123` | Full unrestricted access to all 24 modules, settings, users, roles, permissions, and period reopening |
| **Store Manager** | `manager` | `Manager@123` | Full store operations, price overrides, high discounts, reports, day closing & reopening |
| **Cashier** | `cashier` | `Cashier@123` | POS Terminal, Sales Invoices, Sales Returns, Customer lookup, and Dashboard |
| **Inventory Clerk** | `inventory` | `Inventory@123` | Products, Categories, Suppliers, Purchase Orders, Goods Receiving (GRN), Purchase Returns, Stock Ledger & Adjustments |
| **Accountant** | `accountant` | `Accountant@123` | Payments, Expenses, Customer/Supplier Ledgers, Day Closing, Monthly Closing, Financial Reports & Audit Logs |

*(Click any Demo Credential card on the Login screen to auto-fill and sign in immediately.)*

---

## Architecture & Technology Stack

- **Framework:** ASP.NET Core .NET 8 MVC (`Controllers` + `Views`) — single deployable web application (no separate SPA or external API project)
- **UI Layer:** Razor Views (`.cshtml`), Bootstrap 5.3, Bootstrap Icons, Chart.js, custom responsive CSS (`wwwroot/css/site.css`), and vanilla JavaScript (`wwwroot/js/pos.js`, `wwwroot/js/site.js`)
- **ORM & Data Access:** Entity Framework Core 8 (`Microsoft.EntityFrameworkCore.Sqlite` 8.0.11) Code-First with Migrations (`POSSystem/Migrations`) and Generic Repository + Service layer
- **Database:** Local SQLite database (`POSSystem/pos_system.db`) automatically migrated and seeded on first startup (`DbInitializer.cs`)
- **Security:** Cookie Authentication (`POSAuthCookie`), PBKDF2-SHA256 salted password hashing (`PasswordHelper.cs`), Antiforgery CSRF protection, and policy-based Granular Permission Authorization (`[RequirePermission]`)
- **Transactional Integrity:** All stock-affecting and financial operations (`CompleteSaleAsync`, `CancelSaleAsync`, `CreateSalesReturnAsync`, `CreateGoodsReceiptAsync`, `CreatePurchaseReturnAsync`, `CreateAdjustmentAsync`, `CreatePaymentAsync`) execute inside explicit database transactions (`BeginTransactionAsync`).

---

## POS Terminal Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **`F2`** | Focus Product Search / Barcode Scanner |
| **`F4`** | Focus Paid / Tendered Amount & Complete Sale |
| **`F6`** | Hold Current Sale Bill |
| **`F7`** | Recall Held Sale Bill |
| **`F8`** | Focus Invoice Discount Input |
| **`F9`** | Clear Current Cart |
| **`ESC`** | Close Open Modal / Refocus Barcode Input |

---

## Building, Testing & Running Locally

```bash
# 1. Build the solution
dotnet build POSSystem.sln

# 2. Run automated xUnit integration & E2E tests
dotnet test POSSystem.sln

# 3. Run the web application
dotnet run --project POSSystem/POSSystem.csproj --urls http://0.0.0.0:5000
```
