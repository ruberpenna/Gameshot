using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GameShot.Tests;

public sealed class AdminTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ApplicationDbContext _db = null!;
    private CartService _carts = null!;
    private OrderService _orders = null!;
    private ProductAdminService _products = null!;

    private Platform _ps5 = null!;
    private ProductVariant _game = null!;  // 40,000원 / 재고 5
    private const string Alice = "alice";

    private static readonly ShippingInfo Home = new()
    {
        RecipientName = "홍길동", Phone = "010-1234-5678", PostalCode = "06236", Address1 = "서울 강남구 테헤란로 1",
    };

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _carts = new CartService(_db, TimeProvider.System);
        _orders = TestServices.Orders(_db, TimeProvider.System, carts: _carts);
        _products = new ProductAdminService(_db);

        _ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        _game = new ProductVariant { Platform = _ps5, Sku = "GAME-PS5", ListPrice = 40000, SalePrice = 40000, Stock = 5 };
        var product = new Product { Title = "게임", Slug = "game" };
        product.Variants.Add(_game);
        _db.Products.Add(product);
        _db.Genres.Add(new Genre { Name = "RPG" });
        _db.Users.Add(new ApplicationUser { Id = Alice, UserName = "alice", Email = "alice@example.test" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---- 주문 처리 ----

    [Fact]
    public async Task 결제완료_주문은_배송준비_발송_배송완료_순서로만_넘어간다()
    {
        var no = await PaidOrder(quantity: 1);

        Assert.False((await _orders.ShipAsync(no, "CJ대한통운", "123")).Success);   // 준비 전에 발송 불가
        Assert.False((await _orders.MarkDeliveredAsync(no)).Success);

        Assert.True((await _orders.StartPreparingAsync(no)).Success);
        Assert.False((await _orders.StartPreparingAsync(no)).Success);             // 두 번 처리 안 됨

        Assert.False((await _orders.ShipAsync(no, "CJ대한통운", " ")).Success);     // 송장번호 필수
        Assert.True((await _orders.ShipAsync(no, " CJ대한통운 ", " 1234567890 ")).Success);
        Assert.True((await _orders.MarkDeliveredAsync(no)).Success);

        var order = await _orders.FindForAdminAsync(no);
        Assert.Equal(OrderStatus.Delivered, order!.Status);
        Assert.Equal(("CJ대한통운", "1234567890"), (order.Courier, order.TrackingNumber));
        Assert.NotNull(order.ShippedAt);
        Assert.NotNull(order.DeliveredAt);
        Assert.Equal("alice@example.test", order.User.Email);
    }

    [Fact]
    public async Task 관리자는_배송준비중_주문까지_취소할_수_있고_재고가_돌아온다()
    {
        var no = await PaidOrder(quantity: 2);
        await _orders.StartPreparingAsync(no);
        Assert.Equal(3, await StockOfGame());

        // 고객은 배송 준비가 시작되면 직접 취소할 수 없다
        Assert.False((await _orders.CancelAsync(Alice, no)).Success);

        Assert.True((await _orders.AdminCancelAsync(no)).Success);
        Assert.Equal(5, await StockOfGame());
        Assert.False((await _orders.AdminCancelAsync(no)).Success);
        Assert.Equal(5, await StockOfGame());
    }

    [Fact]
    public async Task 발송된_주문은_관리자도_취소할_수_없다()
    {
        var no = await PaidOrder(quantity: 1);
        await _orders.StartPreparingAsync(no);
        await _orders.ShipAsync(no, "한진택배", "999");

        Assert.False((await _orders.AdminCancelAsync(no)).Success);
        Assert.Equal(4, await StockOfGame());
    }

    [Fact]
    public async Task 주문_검색은_상태와_주문번호_받는분_이메일로_거른다()
    {
        var paid = await PaidOrder(quantity: 1);
        await _carts.AddAsync(new CartOwner(Alice, null), _game.Id, 1);
        var pending = (await _orders.PlaceOrderAsync(Alice, Home, 40000)).OrderNumber!;

        var onlyPaid = await _orders.SearchForAdminAsync(new AdminOrderFilter { Status = OrderStatus.Paid });
        Assert.Equal([paid], onlyPaid.Items.Select(r => r.OrderNumber));

        Assert.Equal(2, (await _orders.SearchForAdminAsync(new AdminOrderFilter { Q = "홍길동" })).TotalCount);
        Assert.Equal(2, (await _orders.SearchForAdminAsync(new AdminOrderFilter { Q = "alice@" })).TotalCount);
        Assert.Equal([pending], (await _orders.SearchForAdminAsync(new AdminOrderFilter { Q = pending })).Items.Select(r => r.OrderNumber));
    }

    [Fact]
    public async Task 대시보드_매출은_결제가_끝나고_취소되지_않은_주문만_센다()
    {
        await PaidOrder(quantity: 1);                       // 40,000 결제 완료
        var cancelled = await PaidOrder(quantity: 1);       // 40,000 → 취소
        await _orders.AdminCancelAsync(cancelled);
        await _carts.AddAsync(new CartOwner(Alice, null), _game.Id, 1);
        await _orders.PlaceOrderAsync(Alice, Home, 40000);  // 결제 대기

        var view = await new AdminDashboardService(_db, _orders, TimeProvider.System).GetAsync();

        Assert.Equal(3, view.TodayOrders);
        Assert.Equal(40000, view.TodaySales);
        Assert.Equal(40000, view.MonthSales);
        Assert.Equal(1, view.Count(OrderStatus.Paid));
        Assert.Equal(1, view.Count(OrderStatus.Cancelled));
        Assert.Equal(1, view.Count(OrderStatus.PendingPayment));
        // 재고 5 - 결제완료 1 - 결제대기 1 = 3 → 부족 목록에 나온다
        Assert.Equal("GAME-PS5", Assert.Single(view.LowStock).Sku);
    }

    // ---- 상품 관리 ----

    [Fact]
    public async Task 상품_등록_후_옵션을_추가하면_쇼핑몰에_나온다()
    {
        var created = await _products.CreateAsync(new ProductForm { Title = "새 게임", Slug = "new-game", IsPublished = true }, "/uploads/x.jpg");
        Assert.True(created.Success, created.Message);

        var added = await _products.AddVariantAsync(created.Id!.Value, Variant("NEW-PS5", list: 50000, sale: 45000, stock: 7));
        Assert.True(added.Success, added.Message);

        var catalog = new CatalogService(_db, TimeProvider.System);
        var card = Assert.Single((await catalog.SearchAsync(new ProductFilter { Q = "새 게임" })).Items);
        Assert.Equal((45000, 50000, "/uploads/x.jpg"), (card.MinSalePrice!.Value, card.ListPriceAtMin!.Value, card.ThumbnailPath));
    }

    [Fact]
    public async Task URL_식별자와_SKU는_중복될_수_없다()
    {
        var dupSlug = await _products.CreateAsync(new ProductForm { Title = "다른 게임", Slug = "game" }, null);
        Assert.False(dupSlug.Success);

        var productId = (await _products.CreateAsync(new ProductForm { Title = "다른 게임", Slug = "other" }, null)).Id!.Value;
        Assert.False((await _products.AddVariantAsync(productId, Variant("GAME-PS5"))).Success);

        // 자기 자신의 SKU·식별자로 저장하는 것은 괜찮다
        Assert.True((await _products.UpdateVariantAsync(_game.Id, Variant("GAME-PS5", stock: 9))).Success);
        Assert.True((await _products.UpdateAsync(productId, new ProductForm { Title = "이름 변경", Slug = "other" }, null)).Success);
    }

    [Fact]
    public async Task 판매가가_정가보다_크거나_없는_플랫폼이면_저장하지_않는다()
    {
        Assert.False((await _products.UpdateVariantAsync(_game.Id, Variant("GAME-PS5", list: 10000, sale: 20000))).Success);

        var wrongPlatform = Variant("X-1");
        wrongPlatform.PlatformId = 9999;
        Assert.False((await _products.UpdateVariantAsync(_game.Id, wrongPlatform)).Success);

        Assert.Equal(40000, await _db.ProductVariants.Where(v => v.Id == _game.Id).Select(v => v.SalePrice).SingleAsync());
    }

    [Fact]
    public async Task 이미지를_새로_올리지_않으면_기존_대표_이미지를_유지한다()
    {
        var id = (await _products.CreateAsync(new ProductForm { Title = "t", Slug = "t" }, "/uploads/old.jpg")).Id!.Value;

        await _products.UpdateAsync(id, new ProductForm { Title = "t2", Slug = "t" }, null);
        Assert.Equal("/uploads/old.jpg", (await _products.GetAsync(id))!.ThumbnailPath);

        await _products.UpdateAsync(id, new ProductForm { Title = "t2", Slug = "t" }, "/uploads/new.jpg");
        Assert.Equal("/uploads/new.jpg", (await _products.GetAsync(id))!.ThumbnailPath);
    }

    // ---- 이미지 업로드 ----

    [Fact]
    public async Task 이미지는_내용이_진짜_이미지일_때만_저장된다()
    {
        var root = Directory.CreateTempSubdirectory("gameshot-test-");
        try
        {
            var storage = new LocalImageStorage(new FakeEnv(root.FullName), new ConfigurationBuilder().Build());
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

            var ok = await storage.SaveProductImageAsync(new MemoryStream(png), "cover.PNG");
            Assert.True(ok.Success, ok.Error);
            Assert.StartsWith("/uploads/products/", ok.Path);
            Assert.EndsWith(".png", ok.Path);
            Assert.True(File.Exists(Path.Combine(root.FullName, ok.Path!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));

            // 확장자만 png인 스크립트 파일
            Assert.False((await storage.SaveProductImageAsync(new MemoryStream("<script>"u8.ToArray()), "evil.png")).Success);
            // 허용하지 않는 확장자
            Assert.False((await storage.SaveProductImageAsync(new MemoryStream(png), "cover.svg")).Success);
            // 너무 큰 파일
            var big = new byte[LocalImageStorage.MaxBytes + 1];
            png.CopyTo(big, 0);
            Assert.False((await storage.SaveProductImageAsync(new MemoryStream(big), "big.png")).Success);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private async Task<string> PaidOrder(int quantity)
    {
        await _carts.AddAsync(new CartOwner(Alice, null), _game.Id, quantity);
        var total = (await _carts.GetViewAsync(new CartOwner(Alice, null))).Total;
        var no = (await _orders.PlaceOrderAsync(Alice, Home, total)).OrderNumber!;
        await _orders.CompletePaymentAsync(Alice, no);
        return no;
    }

    private VariantForm Variant(string sku, int list = 40000, int sale = 40000, int stock = 5) => new()
    {
        PlatformId = _ps5.Id, Sku = sku, ListPrice = list, SalePrice = sale, Stock = stock,
    };

    private Task<int> StockOfGame() =>
        _db.ProductVariants.Where(v => v.Id == _game.Id).Select(v => v.Stock).SingleAsync();

    private sealed class FakeEnv(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "GameShot.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Test";
    }
}
