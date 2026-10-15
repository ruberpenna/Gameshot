using System.ComponentModel.DataAnnotations;
using GameShot.Web.Data;
using GameShot.Web.Models.Catalog;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public class ProductForm
{
    [Display(Name = "상품명")]
    [Required(ErrorMessage = "{0}을 입력하세요.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    [Display(Name = "URL 식별자")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [StringLength(200)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "영문 소문자·숫자·하이픈만 쓸 수 있습니다. (예: elden-ring)")]
    public string Slug { get; set; } = "";

    [Display(Name = "퍼블리셔")]
    [StringLength(100)]
    public string? Publisher { get; set; }

    [Display(Name = "발매일")]
    public DateOnly? ReleaseDate { get; set; }

    [Display(Name = "장르")]
    public int? GenreId { get; set; }

    [Display(Name = "한 줄 소개")]
    [StringLength(500)]
    public string? Summary { get; set; }

    [Display(Name = "상세 설명")]
    public string? Description { get; set; }

    [Display(Name = "공개")]
    public bool IsPublished { get; set; } = true;

    public static ProductForm From(Product p) => new()
    {
        Title = p.Title,
        Slug = p.Slug,
        Publisher = p.Publisher,
        ReleaseDate = p.ReleaseDate,
        GenreId = p.GenreId,
        Summary = p.Summary,
        Description = p.Description,
        IsPublished = p.IsPublished,
    };
}

public class VariantForm
{
    [Display(Name = "플랫폼")]
    [Required(ErrorMessage = "{0}을 고르세요.")]
    public int? PlatformId { get; set; }

    [Display(Name = "SKU")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [StringLength(50)]
    [RegularExpression("^[A-Z0-9][A-Z0-9-]*$", ErrorMessage = "영문 대문자·숫자·하이픈만 쓸 수 있습니다.")]
    public string Sku { get; set; } = "";

    [Display(Name = "에디션")]
    [Required(ErrorMessage = "{0}을 입력하세요.")]
    [StringLength(100)]
    public string Edition { get; set; } = "일반판";

    [Display(Name = "언어")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [StringLength(30)]
    public string Language { get; set; } = "한글판";

    [Display(Name = "상태")]
    public ItemCondition Condition { get; set; } = ItemCondition.New;

    [Display(Name = "정가")]
    [Range(0, 10_000_000, ErrorMessage = "{0}는 {1}~{2}원 사이로 입력하세요.")]
    public int ListPrice { get; set; }

    [Display(Name = "판매가")]
    [Range(0, 10_000_000, ErrorMessage = "{0}는 {1}~{2}원 사이로 입력하세요.")]
    public int SalePrice { get; set; }

    [Display(Name = "재고")]
    [Range(0, 100_000, ErrorMessage = "{0}는 {1}~{2}개 사이로 입력하세요.")]
    public int Stock { get; set; }

    [Display(Name = "판매중")]
    public bool IsActive { get; set; } = true;

    public static VariantForm From(ProductVariant v) => new()
    {
        PlatformId = v.PlatformId,
        Sku = v.Sku,
        Edition = v.Edition,
        Language = v.Language,
        Condition = v.Condition,
        ListPrice = v.ListPrice,
        SalePrice = v.SalePrice,
        Stock = v.Stock,
        IsActive = v.IsActive,
    };
}

public record AdminResult(bool Success, string Message, int? Id = null)
{
    public static AdminResult Ok(string message, int? id = null) => new(true, message, id);
    public static AdminResult Fail(string message) => new(false, message);
}

public record AdminProductRow(
    int Id,
    string Title,
    string Slug,
    string? ThumbnailPath,
    bool IsPublished,
    int VariantCount,
    int TotalStock,
    int? MinSalePrice,
    DateOnly? ReleaseDate);

/// <summary>관리자 상품·SKU 등록/수정. SKU는 장바구니·주문이 참조하므로 지우지 않고 판매중지로 내린다.</summary>
public class ProductAdminService(ApplicationDbContext db)
{
    public const int PageSize = 20;

    public async Task<PagedResult<AdminProductRow>> ListAsync(string? q, int page, CancellationToken ct = default)
    {
        var products = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            products = products.Where(p => p.Title.Contains(term) || p.Slug.Contains(term)
                || p.Variants.Any(v => v.Sku.Contains(term)));
        }

        var total = await products.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        var rows = await products
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new AdminProductRow(
                p.Id, p.Title, p.Slug, p.ThumbnailPath, p.IsPublished,
                p.Variants.Count,
                p.Variants.Where(v => v.IsActive).Sum(v => v.Stock),
                p.Variants.Where(v => v.IsActive).Min(v => (int?)v.SalePrice),
                p.ReleaseDate))
            .ToListAsync(ct);
        return new PagedResult<AdminProductRow>(rows, page, PageSize, total);
    }

    public Task<Product?> GetAsync(int id, CancellationToken ct = default) =>
        db.Products
            .AsNoTracking()
            .Include(p => p.Variants.OrderBy(v => v.Platform.SortOrder).ThenBy(v => v.Id))
                .ThenInclude(v => v.Platform)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<AdminResult> CreateAsync(ProductForm form, string? thumbnailPath, CancellationToken ct = default)
    {
        if (await ValidateProductAsync(form, null, ct) is { } error)
        {
            return AdminResult.Fail(error);
        }

        var product = new Product { Title = "", Slug = "" };
        Apply(product, form);
        product.ThumbnailPath = thumbnailPath;
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok("상품을 등록했습니다. 이제 판매 옵션(SKU)을 추가하세요.", product.Id);
    }

    /// <param name="thumbnailPath">새 이미지를 올렸을 때만 값이 있다 (null이면 기존 이미지 유지)</param>
    public async Task<AdminResult> UpdateAsync(int id, ProductForm form, string? thumbnailPath, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return AdminResult.Fail("상품이 없습니다.");
        }
        if (await ValidateProductAsync(form, id, ct) is { } error)
        {
            return AdminResult.Fail(error);
        }

        Apply(product, form);
        if (thumbnailPath is not null)
        {
            product.ThumbnailPath = thumbnailPath;
        }
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok("상품 정보를 저장했습니다.", id);
    }

    public async Task<AdminResult> AddVariantAsync(int productId, VariantForm form, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return AdminResult.Fail("상품이 없습니다.");
        }
        if (await ValidateVariantAsync(form, null, ct) is { } error)
        {
            return AdminResult.Fail(error);
        }

        var variant = new ProductVariant { ProductId = productId, Sku = "" };
        Apply(variant, form);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok($"옵션 {variant.Sku}을 추가했습니다.", variant.Id);
    }

    public async Task<AdminResult> UpdateVariantAsync(int variantId, VariantForm form, CancellationToken ct = default)
    {
        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null)
        {
            return AdminResult.Fail("옵션이 없습니다.");
        }
        if (await ValidateVariantAsync(form, variantId, ct) is { } error)
        {
            return AdminResult.Fail(error);
        }

        Apply(variant, form);
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok($"옵션 {variant.Sku}을 저장했습니다.", variant.ProductId);
    }

    private async Task<string?> ValidateProductAsync(ProductForm form, int? id, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Slug == form.Slug && p.Id != id, ct))
        {
            return $"URL 식별자 '{form.Slug}'는 이미 다른 상품이 쓰고 있습니다.";
        }
        if (form.GenreId is { } genreId && !await db.Genres.AnyAsync(g => g.Id == genreId, ct))
        {
            return "없는 장르입니다.";
        }
        return null;
    }

    private async Task<string?> ValidateVariantAsync(VariantForm form, int? id, CancellationToken ct)
    {
        if (form.SalePrice > form.ListPrice)
        {
            return "판매가는 정가보다 클 수 없습니다.";
        }
        if (!await db.Platforms.AnyAsync(p => p.Id == form.PlatformId, ct))
        {
            return "없는 플랫폼입니다.";
        }
        if (await db.ProductVariants.AnyAsync(v => v.Sku == form.Sku && v.Id != id, ct))
        {
            return $"SKU '{form.Sku}'는 이미 쓰고 있습니다.";
        }
        return null;
    }

    private static void Apply(Product p, ProductForm f)
    {
        p.Title = f.Title.Trim();
        p.Slug = f.Slug.Trim();
        p.Publisher = Blank(f.Publisher);
        p.ReleaseDate = f.ReleaseDate;
        p.GenreId = f.GenreId;
        p.Summary = Blank(f.Summary);
        p.Description = Blank(f.Description);
        p.IsPublished = f.IsPublished;
    }

    private static void Apply(ProductVariant v, VariantForm f)
    {
        v.PlatformId = f.PlatformId!.Value;
        v.Sku = f.Sku.Trim();
        v.Edition = f.Edition.Trim();
        v.Language = f.Language.Trim();
        v.Condition = f.Condition;
        v.ListPrice = f.ListPrice;
        v.SalePrice = f.SalePrice;
        v.Stock = f.Stock;
        v.IsActive = f.IsActive;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
