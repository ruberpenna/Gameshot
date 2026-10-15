using GameShot.Web.Data;
using GameShot.Web.Models.Catalog;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public enum ProductSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    Title,
}

public record ProductFilter
{
    public string? Platform { get; init; }
    public int? GenreId { get; init; }
    public string? Q { get; init; }
    public ProductSort Sort { get; init; } = ProductSort.Newest;
    public bool InStockOnly { get; init; }
    public int Page { get; init; } = 1;
}

/// <summary>목록에 보여줄 상품 카드 (가격은 판매 중인 SKU 중 최저가)</summary>
public record ProductCard(
    int Id,
    string Slug,
    string Title,
    string? ThumbnailPath,
    DateOnly? ReleaseDate,
    int? MinSalePrice,
    int? ListPriceAtMin,
    bool InStock,
    IReadOnlyList<string> Platforms)
{
    public int DiscountRate =>
        MinSalePrice is { } sale && ListPriceAtMin is { } list && list > 0 && sale < list
            ? (list - sale) * 100 / list
            : 0;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public class CatalogService(ApplicationDbContext db, TimeProvider clock)
{
    public const int PageSize = 12;

    /// <summary>한국 시간 기준 오늘 (예약판매 판단용)</summary>
    public DateOnly Today => KoreanTime.Today(clock);

    public async Task<PagedResult<ProductCard>> SearchAsync(ProductFilter filter, CancellationToken ct = default)
    {
        var products = db.Products.AsNoTracking().Where(p => p.IsPublished && p.Variants.Any(v => v.IsActive));

        if (!string.IsNullOrWhiteSpace(filter.Platform))
        {
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.Platform.Code == filter.Platform));
        }
        if (filter.GenreId is { } genreId)
        {
            products = products.Where(p => p.GenreId == genreId);
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var q = filter.Q.Trim();
            products = products.Where(p => p.Title.Contains(q) || (p.Publisher != null && p.Publisher.Contains(q)));
        }
        if (filter.InStockOnly)
        {
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.Stock > 0));
        }

        // 1) 상품 단위 값은 스칼라 서브쿼리로만 계산 (SQLite 테스트에서도 돌도록 APPLY를 쓰지 않음)
        var cards = products.Select(p => new
        {
            p.Id,
            p.Slug,
            p.Title,
            p.ThumbnailPath,
            p.ReleaseDate,
            MinSalePrice = p.Variants.Where(v => v.IsActive).Min(v => (int?)v.SalePrice),
            ListPriceAtMin = p.Variants
                .Where(v => v.IsActive && v.SalePrice == p.Variants.Where(x => x.IsActive).Min(x => x.SalePrice))
                .Min(v => (int?)v.ListPrice),
            InStock = p.Variants.Any(v => v.IsActive && v.Stock > 0),
        });

        cards = filter.Sort switch
        {
            ProductSort.PriceAsc => cards.OrderBy(c => c.MinSalePrice).ThenBy(c => c.Id),
            ProductSort.PriceDesc => cards.OrderByDescending(c => c.MinSalePrice).ThenBy(c => c.Id),
            ProductSort.Title => cards.OrderBy(c => c.Title).ThenBy(c => c.Id),
            _ => cards.OrderByDescending(c => c.ReleaseDate).ThenByDescending(c => c.Id),
        };

        var total = await products.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));

        var rows = await cards.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);

        // 2) 이 페이지 상품들의 판매 플랫폼만 따로 조회
        var ids = rows.Select(r => r.Id).ToList();
        var platformsByProduct = (await db.ProductVariants
                .AsNoTracking()
                .Where(v => v.IsActive && ids.Contains(v.ProductId))
                .Select(v => new { v.ProductId, v.Platform.Name, v.Platform.SortOrder })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(x => x.SortOrder).Select(x => x.Name).ToList());

        var items = rows
            .Select(r => new ProductCard(
                r.Id, r.Slug, r.Title, r.ThumbnailPath, r.ReleaseDate,
                r.MinSalePrice, r.ListPriceAtMin, r.InStock,
                platformsByProduct.GetValueOrDefault(r.Id) ?? []))
            .ToList();

        return new PagedResult<ProductCard>(items, page, PageSize, total);
    }

    public Task<Product?> FindBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Genre)
            .Include(p => p.Images.OrderBy(i => i.SortOrder))
            .Include(p => p.Variants.Where(v => v.IsActive).OrderBy(v => v.Platform.SortOrder).ThenBy(v => v.SalePrice))
                .ThenInclude(v => v.Platform)
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, ct);

    public Task<List<Platform>> GetPlatformsAsync(CancellationToken ct = default) =>
        db.Platforms.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);

    public Task<List<Genre>> GetGenresAsync(CancellationToken ct = default) =>
        db.Genres.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync(ct);
}
