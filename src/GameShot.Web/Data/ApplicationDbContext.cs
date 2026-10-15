using GameShot.Web.Models;
using GameShot.Web.Models.Benefits;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Mail;
using GameShot.Web.Models.Ordering;
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
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<UserCoupon> UserCoupons => Set<UserCoupon>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.Nickname).HasMaxLength(30);
            // 닉네임은 겹칠 수 없다 (닉네임이 없는 기존 회원은 제외)
            e.HasIndex(u => u.Nickname).IsUnique().HasFilter("[Nickname] IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_AspNetUsers_PointBalance", "[PointBalance] >= 0"));
        });

        builder.Entity<Coupon>(e =>
        {
            e.Property(c => c.Code).HasMaxLength(30);
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => c.Code).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Coupons_DiscountValue", "[DiscountValue] > 0");
                t.HasCheckConstraint("CK_Coupons_Percent", "[DiscountType] <> 1 OR [DiscountValue] <= 100");
            });
        });

        builder.Entity<UserCoupon>(e =>
        {
            // 쿠폰 하나는 회원당 한 장
            e.HasIndex(u => new { u.CouponId, u.UserId }).IsUnique();
            e.HasIndex(u => new { u.UserId, u.UsedAt });
            e.HasOne(u => u.Coupon).WithMany(c => c.Issued).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(u => u.User).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmailMessage>(e =>
        {
            e.Property(m => m.ToAddress).HasMaxLength(256);
            e.Property(m => m.Kind).HasMaxLength(30);
            e.Property(m => m.Subject).HasMaxLength(200);
            e.Property(m => m.LastError).HasMaxLength(500);
            // 발송 작업이 보낼 메일을 찾는 조회
            e.HasIndex(m => new { m.SentAt, m.NextAttemptAt });
            // 연타 방지 조회
            e.HasIndex(m => new { m.ToAddress, m.Kind, m.CreatedAt });
        });

        builder.Entity<PointTransaction>(e =>
        {
            e.Property(p => p.Description).HasMaxLength(200);
            e.HasIndex(p => new { p.UserId, p.CreatedAt });
            e.HasOne(p => p.User).WithMany().OnDelete(DeleteBehavior.Cascade);
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

        builder.Entity<Order>(e =>
        {
            e.Property(o => o.OrderNumber).HasMaxLength(30);
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => new { o.UserId, o.CreatedAt });
            e.Property(o => o.PaymentMethod).HasMaxLength(30);
            e.Property(o => o.Courier).HasMaxLength(30);
            e.Property(o => o.CancelReason).HasMaxLength(50);
            e.Property(o => o.CouponName).HasMaxLength(100);
            e.Property(o => o.TrackingNumber).HasMaxLength(30);
            e.HasIndex(o => new { o.Status, o.CreatedAt });
            // 회원을 지워도 주문 기록은 남겨야 하므로 연쇄 삭제하지 않는다
            e.HasOne(o => o.User).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.ComplexProperty(o => o.Shipping, s =>
            {
                s.Property(x => x.RecipientName).HasMaxLength(30);
                s.Property(x => x.Phone).HasMaxLength(20);
                s.Property(x => x.PostalCode).HasMaxLength(10);
                s.Property(x => x.Address1).HasMaxLength(200);
                s.Property(x => x.Address2).HasMaxLength(200);
                s.Property(x => x.Memo).HasMaxLength(100);
            });
        });

        builder.Entity<OrderItem>(e =>
        {
            e.Property(i => i.ProductTitle).HasMaxLength(200);
            e.Property(i => i.ProductSlug).HasMaxLength(200);
            e.Property(i => i.ThumbnailPath).HasMaxLength(300);
            e.Property(i => i.PlatformName).HasMaxLength(50);
            e.Property(i => i.Edition).HasMaxLength(100);
            e.Property(i => i.Language).HasMaxLength(30);
            e.HasOne(i => i.Order).WithMany(o => o.Items).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.ProductVariant).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0"));
        });
    }
}
