using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models.Entities;
using POSSystem.Models.ViewModels;

namespace POSSystem.Services;

public interface IAuditService
{
    Task LogAsync(int? userId, string username, string action, string entityName, string? entityId, string? oldValue = null, string? newValue = null, string? ipAddress = null);
    IQueryable<AuditLog> QueryLogs(string? search = null, string? action = null, DateTime? fromDate = null, DateTime? toDate = null);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _context;

    public AuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(int? userId, string username, string action, string entityName, string? entityId, string? oldValue = null, string? newValue = null, string? ipAddress = null)
    {
        var log = new AuditLog
        {
            UserId = userId,
            Username = string.IsNullOrWhiteSpace(username) ? "System" : username,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = DateTime.UtcNow,
            IpAddress = ipAddress ?? "127.0.0.1"
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
    }

    public IQueryable<AuditLog> QueryLogs(string? search = null, string? action = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(a =>
                a.Username.ToLower().Contains(s) ||
                a.Action.ToLower().Contains(s) ||
                a.EntityName.ToLower().Contains(s) ||
                (a.EntityId != null && a.EntityId.ToLower().Contains(s)) ||
                (a.NewValue != null && a.NewValue.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (fromDate.HasValue)
        {
            var start = fromDate.Value.Date;
            query = query.Where(a => a.Timestamp >= start);
        }

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1);
            query = query.Where(a => a.Timestamp < end);
        }

        return query.OrderByDescending(a => a.Timestamp);
    }
}

public interface ISettingsService
{
    Task<string> GetValueAsync(string key, string defaultValue = "");
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task<decimal> GetDecimalAsync(string key, decimal defaultValue = 0m);
    Task<SettingsViewModel> GetSettingsViewModelAsync();
    Task SaveSettingsAsync(SettingsViewModel model, int? userId, string username, string? ipAddress = null);
    Task<string> GenerateDocumentNumberAsync(string prefixKey, string defaultPrefix, int nextSequence);
}

public class SettingsService : ISettingsService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public SettingsService(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<string> GetValueAsync(string key, string defaultValue = "")
    {
        var setting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var val = await GetValueAsync(key, defaultValue.ToString());
        return bool.TryParse(val, out var b) ? b : defaultValue;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal defaultValue = 0m)
    {
        var val = await GetValueAsync(key, defaultValue.ToString());
        return decimal.TryParse(val, out var d) ? d : defaultValue;
    }

    public async Task<SettingsViewModel> GetSettingsViewModelAsync()
    {
        var dict = await _context.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        return new SettingsViewModel
        {
            BusinessName = dict.GetValueOrDefault("BusinessName", "Apex Retail Mart & Superstore"),
            BusinessAddress = dict.GetValueOrDefault("BusinessAddress", ""),
            BusinessPhone = dict.GetValueOrDefault("BusinessPhone", ""),
            BusinessEmail = dict.GetValueOrDefault("BusinessEmail", ""),
            BusinessLogo = dict.GetValueOrDefault("BusinessLogo", "/images/logo.svg"),
            TaxNumber = dict.GetValueOrDefault("TaxNumber", ""),
            InvoiceFooter = dict.GetValueOrDefault("InvoiceFooter", ""),

            DefaultCustomer = dict.GetValueOrDefault("DefaultCustomer", "Walk-in Customer"),
            DefaultPaymentMethod = dict.GetValueOrDefault("DefaultPaymentMethod", "Cash"),
            AutoPrint = bool.TryParse(dict.GetValueOrDefault("AutoPrint", "false"), out var ap) && ap,
            ReceiptFormat = dict.GetValueOrDefault("ReceiptFormat", "Thermal"),
            AllowNegativeStock = bool.TryParse(dict.GetValueOrDefault("AllowNegativeStock", "false"), out var ans) && ans,
            MaximumDiscountPercent = decimal.TryParse(dict.GetValueOrDefault("MaximumDiscountPercent", "20"), out var mdp) ? mdp : 20m,
            RequireCustomerForCreditSale = !bool.TryParse(dict.GetValueOrDefault("RequireCustomerForCreditSale", "true"), out var rccs) || rccs,

            DefaultMinimumStock = decimal.TryParse(dict.GetValueOrDefault("DefaultMinimumStock", "5"), out var dms) ? dms : 5m,
            DefaultReorderLevel = decimal.TryParse(dict.GetValueOrDefault("DefaultReorderLevel", "15"), out var drl) ? drl : 15m,
            NegativeStockPolicy = dict.GetValueOrDefault("NegativeStockPolicy", "Block"),

            EnableTax = !bool.TryParse(dict.GetValueOrDefault("EnableTax", "true"), out var et) || et,
            DefaultTaxRate = decimal.TryParse(dict.GetValueOrDefault("DefaultTaxRate", "5.0"), out var dtr) ? dtr : 5m,

            Prefix_INV = dict.GetValueOrDefault("Prefix_INV", "INV"),
            Prefix_PO = dict.GetValueOrDefault("Prefix_PO", "PO"),
            Prefix_GRN = dict.GetValueOrDefault("Prefix_GRN", "GRN"),
            Prefix_PR = dict.GetValueOrDefault("Prefix_PR", "PR"),
            Prefix_SR = dict.GetValueOrDefault("Prefix_SR", "SR"),
            Prefix_PAY = dict.GetValueOrDefault("Prefix_PAY", "PAY"),
            Prefix_EXP = dict.GetValueOrDefault("Prefix_EXP", "EXP")
        };
    }

    public async Task SaveSettingsAsync(SettingsViewModel model, int? userId, string username, string? ipAddress = null)
    {
        var updates = new Dictionary<string, (string Value, string Group)>
        {
            ["BusinessName"] = (model.BusinessName, "Business"),
            ["BusinessAddress"] = (model.BusinessAddress ?? "", "Business"),
            ["BusinessPhone"] = (model.BusinessPhone ?? "", "Business"),
            ["BusinessEmail"] = (model.BusinessEmail ?? "", "Business"),
            ["BusinessLogo"] = (model.BusinessLogo ?? "/images/logo.svg", "Business"),
            ["TaxNumber"] = (model.TaxNumber ?? "", "Business"),
            ["InvoiceFooter"] = (model.InvoiceFooter ?? "", "Business"),

            ["DefaultCustomer"] = (model.DefaultCustomer ?? "Walk-in Customer", "POS"),
            ["DefaultPaymentMethod"] = (model.DefaultPaymentMethod ?? "Cash", "POS"),
            ["AutoPrint"] = (model.AutoPrint.ToString().ToLower(), "POS"),
            ["ReceiptFormat"] = (model.ReceiptFormat ?? "Thermal", "POS"),
            ["AllowNegativeStock"] = (model.AllowNegativeStock.ToString().ToLower(), "POS"),
            ["MaximumDiscountPercent"] = (model.MaximumDiscountPercent.ToString("0.##"), "POS"),
            ["RequireCustomerForCreditSale"] = (model.RequireCustomerForCreditSale.ToString().ToLower(), "POS"),

            ["DefaultMinimumStock"] = (model.DefaultMinimumStock.ToString("0.##"), "Inventory"),
            ["DefaultReorderLevel"] = (model.DefaultReorderLevel.ToString("0.##"), "Inventory"),
            ["NegativeStockPolicy"] = (model.NegativeStockPolicy ?? "Block", "Inventory"),

            ["EnableTax"] = (model.EnableTax.ToString().ToLower(), "Tax"),
            ["DefaultTaxRate"] = (model.DefaultTaxRate.ToString("0.##"), "Tax"),

            ["Prefix_INV"] = (model.Prefix_INV ?? "INV", "Numbering"),
            ["Prefix_PO"] = (model.Prefix_PO ?? "PO", "Numbering"),
            ["Prefix_GRN"] = (model.Prefix_GRN ?? "GRN", "Numbering"),
            ["Prefix_PR"] = (model.Prefix_PR ?? "PR", "Numbering"),
            ["Prefix_SR"] = (model.Prefix_SR ?? "SR", "Numbering"),
            ["Prefix_PAY"] = (model.Prefix_PAY ?? "PAY", "Numbering"),
            ["Prefix_EXP"] = (model.Prefix_EXP ?? "EXP", "Numbering")
        };

        var existing = await _context.SystemSettings.ToDictionaryAsync(s => s.Key);
        foreach (var kvp in updates)
        {
            if (existing.TryGetValue(kvp.Key, out var setting))
            {
                setting.Value = kvp.Value.Value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.SystemSettings.Add(new SystemSetting
                {
                    Key = kvp.Key,
                    Value = kvp.Value.Value,
                    Group = kvp.Value.Group,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
        await _auditService.LogAsync(userId, username, "UpdateSettings", "SystemSetting", "All", null, $"Updated system settings (AllowNegativeStock={model.AllowNegativeStock}, MaxDiscount={model.MaximumDiscountPercent}%)", ipAddress);
    }

    public async Task<string> GenerateDocumentNumberAsync(string prefixKey, string defaultPrefix, int nextSequence)
    {
        var prefix = await GetValueAsync(prefixKey, defaultPrefix);
        if (string.IsNullOrWhiteSpace(prefix)) prefix = defaultPrefix;
        return $"{prefix}-{DateTime.UtcNow:yyyyMM}-{nextSequence:D4}";
    }
}
