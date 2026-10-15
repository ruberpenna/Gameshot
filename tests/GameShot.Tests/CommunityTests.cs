using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Tests;

/// <summary>찜과 리뷰</summary>
public sealed class CommunityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestClock _clock = new();
    private ApplicationDbContext _db = null!;
    private WishlistService _wishlist = null!;
    private ReviewService _reviews = null!;
    private OrderService _orders = null!;
    private CartService _carts = null!;

    private Product _game = null!;
    private Product _other = null!;
    private Product _hidden = null!;
    private const string Alice = "alice";
    private const string Bob = "bob";

    private static readonly ShippingInfo Home = new()
    {
        RecipientName = "홍길동", Phone = "010-1234-5678", PostalCode = "06236", Address1 = "서울 강남구 테헤란로 1",
    };

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _wishlist = new WishlistService(_db, _clock);
        _reviews = new ReviewService(_db, _clock);
        _carts = new CartService(_db, _clock);
        _orders = TestServices.Orders(_db, _clock, carts: _carts);

        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        _game = Product("게임", "game", ps5, "GAME");
        _other = Product("다른 게임", "other", ps5, "OTHER");
        _hidden = Product("비공개", "hidden", ps5, "HIDDEN");
        _hidden.IsPublished = false;
        _db.Products.AddRange(_game, _other, _hidden);
        _db.Users.AddRange(
            new ApplicationUser { Id = Alice, UserName = "alice", Email = "alice@example.test", Nickname = "앨리스" },
            new ApplicationUser { Id = Bob, UserName = "bob", Email = "bob@example.test" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---- 찜 ----

    [Fact]
    public async Task 찜은_누를_때마다_켜졌다_꺼진다()
    {
        Assert.True(await _wishlist.ToggleAsync(Alice, _game.Id));
        Assert.Equal([_game.Id], await _wishlist.ListProductIdsAsync(Alice));
        Assert.False(await _wishlist.ToggleAsync(Alice, _game.Id));
        Assert.Empty(await _wishlist.ListProductIdsAsync(Alice));
    }

    [Fact]
    public async Task 비공개_상품이나_없는_상품은_찜할_수_없다()
    {
        Assert.Null(await _wishlist.ToggleAsync(Alice, _hidden.Id));
        Assert.Null(await _wishlist.ToggleAsync(Alice, 99999));
        Assert.Empty(_db.WishItems);
    }

    [Fact]
    public async Task 찜_목록은_회원별이고_최근_찜한_순서다()
    {
        await _wishlist.ToggleAsync(Alice, _game.Id);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _wishlist.ToggleAsync(Alice, _other.Id);
        await _wishlist.ToggleAsync(Bob, _game.Id);

        Assert.Equal([_other.Id, _game.Id], await _wishlist.ListProductIdsAsync(Alice));
        Assert.Equal([_game.Id], (await _wishlist.WishedAmongAsync(Bob, [_game.Id, _other.Id])).ToList());
        Assert.Empty(await _wishlist.WishedAmongAsync(null, [_game.Id]));
        Assert.Equal(2, await _wishlist.CountForProductAsync(_game.Id));
    }

    [Fact]
    public async Task 찜_목록에서_상품이_비공개되면_빠진다()
    {
        await _wishlist.ToggleAsync(Alice, _game.Id);
        await _db.Products.Where(p => p.Id == _game.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false));

        Assert.Empty(await _wishlist.ListProductIdsAsync(Alice));
    }

    // ---- 리뷰 자격 ----

    [Fact]
    public async Task 배송_완료된_주문의_상품만_리뷰를_쓸_수_있다()
    {
        Assert.Equal(ReviewEligibility.NotSignedIn, await _reviews.EligibilityAsync(null, _game.Id));
        Assert.Equal(ReviewEligibility.NotPurchased, await _reviews.EligibilityAsync(Alice, _game.Id));

        var no = await Order(Alice, _game);
        Assert.Equal(ReviewEligibility.NotPurchased, await _reviews.EligibilityAsync(Alice, _game.Id));   // 결제만 됨
        await _orders.StartPreparingAsync(no);
        await _orders.ShipAsync(no, "CJ대한통운", "1");
        Assert.Equal(ReviewEligibility.NotPurchased, await _reviews.EligibilityAsync(Alice, _game.Id));   // 배송중
        await _orders.MarkDeliveredAsync(no);

        Assert.Equal(ReviewEligibility.CanWrite, await _reviews.EligibilityAsync(Alice, _game.Id));
        Assert.Equal(ReviewEligibility.NotPurchased, await _reviews.EligibilityAsync(Alice, _other.Id));
        Assert.Equal(ReviewEligibility.NotPurchased, await _reviews.EligibilityAsync(Bob, _game.Id));
        Assert.False((await _reviews.WriteAsync(Bob, _game.Id, 5, "사지도 않았는데 쓰는 리뷰")).Success);
    }

    [Fact]
    public async Task 상품당_리뷰는_하나만_쓸_수_있다()
    {
        await Delivered(Alice, _game);

        Assert.True((await _reviews.WriteAsync(Alice, _game.Id, 5, "정말 재밌는 게임입니다!")).Success);
        Assert.False((await _reviews.WriteAsync(Alice, _game.Id, 1, "두 번째로 쓰는 리뷰입니다")).Success);
        Assert.Equal(ReviewEligibility.AlreadyWritten, await _reviews.EligibilityAsync(Alice, _game.Id));
        Assert.Single(_db.Reviews);
    }

    [Theory]
    [InlineData(0, "열 글자가 넘는 리뷰입니다")]
    [InlineData(6, "열 글자가 넘는 리뷰입니다")]
    [InlineData(5, "짧아요")]
    [InlineData(5, "          공백만 많음   ")]
    public async Task 별점과_글자수를_검사한다(int rating, string content)
    {
        await Delivered(Alice, _game);

        Assert.False((await _reviews.WriteAsync(Alice, _game.Id, rating, content)).Success);
        Assert.Empty(_db.Reviews);
    }

    [Fact]
    public async Task 본인_리뷰만_고치거나_지울_수_있다()
    {
        await Delivered(Alice, _game);
        await _reviews.WriteAsync(Alice, _game.Id, 4, "처음에 쓴 리뷰 내용입니다");
        var id = await _db.Reviews.Select(r => r.Id).SingleAsync();

        Assert.False((await _reviews.EditAsync(Bob, id, 1, "남의 리뷰를 고치려는 시도")).Success);
        Assert.False((await _reviews.DeleteAsync(Bob, id)).Success);

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await _reviews.EditAsync(Alice, id, 2, "고친 리뷰 내용입니다만")).Success);
        var edited = await _db.Reviews.AsNoTracking().SingleAsync();
        Assert.Equal((2, "고친 리뷰 내용입니다만"), (edited.Rating, edited.Content));
        Assert.NotNull(edited.UpdatedAt);

        Assert.True((await _reviews.DeleteAsync(Alice, id)).Success);
        Assert.Equal(ReviewEligibility.CanWrite, await _reviews.EligibilityAsync(Alice, _game.Id));
    }

    // ---- 보여주기 ----

    [Fact]
    public async Task 평균과_분포를_계산하고_숨긴_리뷰는_빼되_작성자에게는_보인다()
    {
        await Delivered(Alice, _game);
        await Delivered(Bob, _game);
        await _reviews.WriteAsync(Alice, _game.Id, 5, "최고의 게임이에요 강추");
        await _reviews.WriteAsync(Bob, _game.Id, 2, "생각보다 별로였습니다");

        var all = await _reviews.GetForProductAsync(_game.Id, null, 1);
        Assert.Equal((3.5, 2), (all.Summary.Average, all.Summary.Count));
        Assert.Equal(50, all.Summary.Percent(5));
        Assert.Equal(["bob***", "앨리스"], all.Page.Items.Select(r => r.AuthorName).Order(StringComparer.Ordinal));
        Assert.All(all.Page.Items, r => Assert.Equal("PlayStation 5 · 일반판", r.PurchasedOption));

        var bobsReview = await _db.Reviews.Where(r => r.UserId == Bob).Select(r => r.Id).SingleAsync();
        await _reviews.SetHiddenAsync(bobsReview, true);

        var afterHide = await _reviews.GetForProductAsync(_game.Id, Alice, 1);
        Assert.Equal((5.0, 1), (afterHide.Summary.Average, afterHide.Summary.Count));
        Assert.True(Assert.Single(afterHide.Page.Items).IsMine);

        var forBob = await _reviews.GetForProductAsync(_game.Id, Bob, 1);
        Assert.NotNull(forBob.MyReview);
        Assert.DoesNotContain(forBob.Page.Items, r => r.IsMine);

        var catalog = new CatalogService(_db, _clock);
        var card = (await catalog.SearchAsync(new ProductFilter { Q = "게임" })).Items.Single(c => c.Id == _game.Id);
        Assert.Equal((5.0, 1), (card.AverageRating!.Value, card.ReviewCount));
    }

    [Fact]
    public async Task 리뷰는_최신순으로_페이지를_나눈다()
    {
        for (var i = 0; i < ReviewService.PageSize + 2; i++)
        {
            var userId = $"user{i}";
            _db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, Email = $"{userId}@example.test" });
            await _db.SaveChangesAsync();
            await Delivered(userId, _game);
            _clock.Advance(TimeSpan.FromMinutes(1));
            await _reviews.WriteAsync(userId, _game.Id, 4, $"리뷰 번호 {i:00} 입니다");
        }

        var first = await _reviews.GetForProductAsync(_game.Id, null, 1);
        var second = await _reviews.GetForProductAsync(_game.Id, null, 2);

        Assert.Equal(ReviewService.PageSize, first.Page.Items.Count);
        Assert.Equal(2, second.Page.Items.Count);
        Assert.Contains("11", first.Page.Items[0].Content);
        Assert.Contains("00", second.Page.Items[^1].Content);
    }

    private static Product Product(string title, string slug, Platform platform, string sku)
    {
        var product = new Product { Title = title, Slug = slug };
        product.Variants.Add(new ProductVariant { Platform = platform, Sku = sku, ListPrice = 40000, SalePrice = 40000, Stock = 100 });
        return product;
    }

    private async Task<string> Order(string userId, Product product)
    {
        var variantId = await _db.ProductVariants.Where(v => v.ProductId == product.Id).Select(v => v.Id).SingleAsync();
        await _carts.AddAsync(new CartOwner(userId, null), variantId, 1);
        var no = (await _orders.PlaceOrderAsync(userId, Home, 40000)).OrderNumber!;
        await _orders.CompletePaymentAsync(userId, no);
        return no;
    }

    private async Task Delivered(string userId, Product product)
    {
        var no = await Order(userId, product);
        await _orders.StartPreparingAsync(no);
        await _orders.ShipAsync(no, "CJ대한통운", "1");
        await _orders.MarkDeliveredAsync(no);
    }
}
