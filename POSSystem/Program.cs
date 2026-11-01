using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using POSSystem.Authorization;
using POSSystem.Data;
using POSSystem.Data.Seed;
using POSSystem.Middleware;
using POSSystem.Repositories;
using POSSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Forwarded Headers for reverse-proxy / E2B preview environment
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Database: SQLite + EF Core 8
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=possystem.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ApexPOS.Auth";
        options.Cookie.HttpOnly = true;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Granular Permission Authorization
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// Antiforgery: suppress X-Frame-Options: SAMEORIGIN so preview iframe works cleanly
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "ApexPOS.Antiforgery";
    options.HeaderName = "RequestVerificationToken";
    options.SuppressXFrameOptionsHeader = true;
});

// Register Repositories & Services
builder.Services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IDayClosingService, DayClosingService>();
builder.Services.AddScoped<IMonthlyClosingService, MonthlyClosingService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IGoodsReceivingService, GoodsReceivingService>();
builder.Services.AddScoped<IPurchaseReturnService, PurchaseReturnService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<ISalesReturnService, SalesReturnService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IReportingService, ReportingService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseForwardedHeaders();

// Adaptive Cookie Policy so cookies work both in HTTPS cross-site preview iframes and local HTTP
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Unspecified,
    OnAppendCookie = cookieContext =>
    {
        if (cookieContext.Context.Request.IsHttps ||
            string.Equals(cookieContext.Context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase))
        {
            cookieContext.CookieOptions.SameSite = SameSiteMode.None;
            cookieContext.CookieOptions.Secure = true;
        }
        else
        {
            cookieContext.CookieOptions.SameSite = SameSiteMode.Lax;
        }
    }
});

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// Initialize and seed SQLite database automatically
await DbInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();

public partial class Program { }
