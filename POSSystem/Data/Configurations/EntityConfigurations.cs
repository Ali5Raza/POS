using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Models.Entities;

namespace POSSystem.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(p => p.SKU).IsUnique();
        builder.HasIndex(p => p.Barcode).IsUnique();
        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DefaultSupplier)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.DefaultSupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.HasIndex(c => c.Name);
        builder.HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SecurityConfiguration :
    IEntityTypeConfiguration<ApplicationUser>,
    IEntityTypeConfiguration<Role>,
    IEntityTypeConfiguration<Permission>,
    IEntityTypeConfiguration<RolePermission>,
    IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email);
        builder.HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasIndex(r => r.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasIndex(p => p.Code).IsUnique();
    }

    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public class TransactionConfiguration :
    IEntityTypeConfiguration<PurchaseOrder>,
    IEntityTypeConfiguration<GoodsReceipt>,
    IEntityTypeConfiguration<PurchaseReturn>,
    IEntityTypeConfiguration<Sale>,
    IEntityTypeConfiguration<SalesReturn>,
    IEntityTypeConfiguration<Payment>,
    IEntityTypeConfiguration<Expense>,
    IEntityTypeConfiguration<StockMovement>,
    IEntityTypeConfiguration<DayClosing>,
    IEntityTypeConfiguration<MonthlyClosing>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.HasIndex(po => po.PONumber).IsUnique();
        builder.HasIndex(po => po.OrderDate);
        builder.HasOne(po => po.Supplier)
            .WithMany(s => s.PurchaseOrders)
            .HasForeignKey(po => po.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.HasIndex(g => g.GRNNumber).IsUnique();
        builder.HasIndex(g => g.ReceivingDate);
        builder.HasOne(g => g.Supplier)
            .WithMany(s => s.GoodsReceipts)
            .HasForeignKey(g => g.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.PurchaseOrder)
            .WithMany(po => po.GoodsReceipts)
            .HasForeignKey(g => g.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.HasIndex(pr => pr.ReturnNumber).IsUnique();
        builder.HasOne(pr => pr.GoodsReceipt)
            .WithMany(g => g.PurchaseReturns)
            .HasForeignKey(pr => pr.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(pr => pr.Supplier)
            .WithMany(s => s.PurchaseReturns)
            .HasForeignKey(pr => pr.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.HasIndex(s => s.SaleDate);
        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.HasIndex(sr => sr.ReturnNumber).IsUnique();
        builder.HasOne(sr => sr.Sale)
            .WithMany(s => s.SalesReturns)
            .HasForeignKey(sr => sr.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasIndex(p => p.PaymentNumber).IsUnique();
        builder.HasIndex(p => p.PaymentDate);
    }

    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasIndex(e => e.ExpenseNumber).IsUnique();
        builder.HasIndex(e => e.ExpenseDate);
    }

    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasIndex(sm => sm.ProductId);
        builder.HasIndex(sm => sm.CreatedAt);
        builder.HasIndex(sm => sm.ReferenceNumber);
    }

    public void Configure(EntityTypeBuilder<DayClosing> builder)
    {
        builder.HasIndex(dc => dc.ClosingDate).IsUnique();
    }

    public void Configure(EntityTypeBuilder<MonthlyClosing> builder)
    {
        builder.HasIndex(mc => new { mc.Year, mc.Month }).IsUnique();
    }
}
