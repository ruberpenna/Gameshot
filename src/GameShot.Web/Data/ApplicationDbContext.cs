using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Shopping;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.Nickname).HasMaxLength(30);
        });

        builder.Entity<Platform>(e =>
        {
            e.Property(p => p.Code).HasMaxLength(20);
            e.Property(p => p.Name).HasMaxLength(50);
            e.HasIndex(p => p.Code).IsUnique();
        });

        builder.Entity<Genre>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(50);
            e.HasIndex(g => g.Name).IsUnique();
        });

        builder.Entity<Product>(e =>
        {
            e.Property(p => p.Title).HasMaxLength(200);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Publisher).HasMaxLength(100);
            e.Property(p => p.Summary).HasMaxLength(500);
            e.Property(p => p.ThumbnailPath).HasMaxLength(300);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.ReleaseDate);
            e.HasOne(p => p.Genre).WithMany(g => g.Products).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProductVariant>(e =>
        {
            e.Property(v => v.Sku).HasMaxLength(50);
            e.Property(v => v.Edition).HasMaxLength(100);
            e.Property(v => v.Language).HasMaxLength(30);
            e.HasIndex(v => v.Sku).IsUnique();
            e.HasOne(v => v.Product).WithMany(p => p.Variants).OnDelete(DeleteBehavior.Cascade);
            // 플랫폼에 상품이 걸려 있으면 플랫폼을 지울 수 없게
            e.HasOne(v => v.Platform).WithMany(p => p.Variants).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ProductVariants_Price", "[ListPrice] >= 0 AND [SalePrice] >= 0");
                t.HasCheckConstraint("CK_ProductVariants_Stock", "[Stock] >= 0");
            });
        });

        builder.Entity<ProductImage>(e =>
        {
            e.Property(i => i.Path).HasMaxLength(300);
            e.HasOne(i => i.Product).WithMany(p => p.Images).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Cart>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
            // 회원당 장바구니 하나 (비회원 장바구니는 UserId가 null이라 제외)
            e.HasIndex(c => c.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(c => c.UpdatedAt);
            e.HasOne(c => c.User).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CartItem>(e =>
        {
            e.HasIndex(i => new { i.CartId, i.ProductVariantId }).IsUnique();
            e.HasOne(i => i.Cart).WithMany(c => c.Items).OnDelete(DeleteBehavior.Cascade);
            // 장바구니에 담긴 SKU는 지우지 말고 판매 중지(IsActive=false)로 내린다
            e.HasOne(i => i.ProductVariant).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0"));
        });
    }
}
