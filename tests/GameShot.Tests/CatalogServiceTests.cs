using GameShot.Web.Data;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Tests;

/// <summary>SQLite 메모리 DB로 CatalogService의 검색 조건을 검증</summary>
public sealed class CatalogServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ApplicationDbContext _db = null!;
    private CatalogService _catalog = null!;

    // 2026-10-15 09:00 KST
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _catalog = new CatalogService(_db, _clock);

        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5", SortOrder = 0 };
        var sw2 = new Platform { Code = "switch2", Name = "Nintendo Switch 2", SortOrder = 1 };
        var rpg = new Genre { Name = "RPG" };
        var action = new Genre { Name = "액션" };

        _db.Products.AddRange(
            Product("엘든 링", "elden-ring", rpg, new DateOnly(2022, 2, 25),
                Variant(ps5, "ER-PS5", 64800, 54800, stock: 3)),
            Product("젤다의 전설", "zelda", action, new DateOnly(2025, 6, 5),
                Variant(sw2, "ZD-SW2", 79800, 79800, stock: 0)),
            Product("멀티 플랫폼", "multi", action, new DateOnly(2024, 1, 1),
                Variant(ps5, "MP-PS5", 70000, 70000, stock: 1),
                Variant(sw2, "MP-SW2", 60000, 50000, stock: 1)),
            Product("발매 예정작", "upcoming", rpg, new DateOnly(2027, 3, 1),
                Variant(ps5, "UP-PS5", 89000, 89000, stock: 10)),
            Product("비공개 상품", "hidden", rpg, new DateOnly(2023, 1, 1),
                Variant(ps5, "HD-PS5", 10000, 10000, stock: 1), published: false),
            Product("판매 종료", "inactive", rpg, new DateOnly(2023, 1, 1),
                Variant(ps5, "IN-PS5", 10000, 10000, stock: 1, active: false)));
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task 비공개이거나_판매중인_옵션이_없는_상품은_제외된다()
    {
        var result = await _catalog.SearchAsync(new ProductFilter());

        Assert.Equal(4, result.TotalCount);
        Assert.DoesNotContain(result.Items, c => c.Slug is "hidden" or "inactive");
    }

    [Fact]
    public async Task 플랫폼으로_거르면_그_플랫폼_SKU가_있는_상품만_나온다()
    {
        var result = await _catalog.SearchAsync(new ProductFilter { Platform = "switch2" });

        Assert.Equal(["multi", "zelda"], result.Items.Select(c => c.Slug).Order());
    }

    [Fact]
    public async Task 카드_가격은_판매중인_SKU의_최저가와_그_정가다()
    {
        var result = await _catalog.SearchAsync(new ProductFilter { Q = "멀티" });

        var card = Assert.Single(result.Items);
        Assert.Equal(50000, card.MinSalePrice);
        Assert.Equal(60000, card.ListPriceAtMin);
        Assert.Equal(16, card.DiscountRate);
        Assert.Equal(["PlayStation 5", "Nintendo Switch 2"], card.Platforms);
    }

    [Fact]
    public async Task 재고있는_상품만_보기는_품절_상품을_뺀다()
    {
        var result = await _catalog.SearchAsync(new ProductFilter { InStockOnly = true });

        Assert.DoesNotContain(result.Items, c => c.Slug == "zelda");
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task 낮은_가격순_정렬()
    {
        var result = await _catalog.SearchAsync(new ProductFilter { Sort = ProductSort.PriceAsc });

        Assert.Equal(["multi", "elden-ring", "zelda", "upcoming"], result.Items.Select(c => c.Slug));
    }

    [Fact]
    public async Task 기본_정렬은_최신_발매순()
    {
        var result = await _catalog.SearchAsync(new ProductFilter());

        Assert.Equal(["upcoming", "zelda", "multi", "elden-ring"], result.Items.Select(c => c.Slug));
    }

    [Fact]
    public async Task 범위를_벗어난_페이지는_마지막_페이지로_맞춘다()
    {
        var result = await _catalog.SearchAsync(new ProductFilter { Page = 99 });

        Assert.Equal(1, result.Page);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public async Task 상세는_판매중인_옵션만_가져온다()
    {
        var product = await _catalog.FindBySlugAsync("multi");

        Assert.NotNull(product);
        Assert.Equal(2, product.Variants.Count);
        Assert.All(product.Variants, v => Assert.NotNull(v.Platform));
        Assert.Null(await _catalog.FindBySlugAsync("hidden"));
    }

    [Fact]
    public void 오늘은_한국시간_기준이고_발매일이_지나지_않으면_예약판매다()
    {
        Assert.Equal(new DateOnly(2026, 10, 15), _catalog.Today);

        // UTC 15:00 = KST 다음 날 00:00
        _clock.Now = new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 16), _catalog.Today);

        var product = new Product { Title = "t", Slug = "t", ReleaseDate = new DateOnly(2026, 10, 16) };
        Assert.True(product.IsPreorder(new DateOnly(2026, 10, 15)));
        Assert.False(product.IsPreorder(new DateOnly(2026, 10, 16)));
    }

    private static Product Product(string title, string slug, Genre genre, DateOnly release,
        params ProductVariant[] variants) => Product(title, slug, genre, release, variants[0], true, variants[1..]);

    private static Product Product(string title, string slug, Genre genre, DateOnly release,
        ProductVariant first, bool published, params ProductVariant[] rest)
    {
        var product = new Product { Title = title, Slug = slug, Genre = genre, ReleaseDate = release, IsPublished = published };
        foreach (var v in rest.Prepend(first))
        {
            product.Variants.Add(v);
        }
        return product;
    }

    private static ProductVariant Variant(Platform platform, string sku, int list, int sale, int stock, bool active = true) =>
        new() { Platform = platform, Sku = sku, ListPrice = list, SalePrice = sale, Stock = stock, IsActive = active };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
