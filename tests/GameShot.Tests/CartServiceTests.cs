using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Tests;

public sealed class CartServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ApplicationDbContext _db = null!;
    private CartService _carts = null!;

    private ProductVariant _game = null!;      // 판매가 40,000 / 재고 5
    private ProductVariant _cheap = null!;     // 판매가 9,000 / 재고 20
    private ProductVariant _soldOut = null!;   // 재고 0
    private ProductVariant _inactive = null!;  // 판매 중지
    private const string UserId = "user-1";

    private static readonly CartOwner NoCart = new(null, null);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _carts = new CartService(_db, TimeProvider.System);

        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        _game = Variant(ps5, "GAME", 40000, stock: 5);
        _cheap = Variant(ps5, "CHEAP", 9000, stock: 20);
        _soldOut = Variant(ps5, "SOLD", 50000, stock: 0);
        _inactive = Variant(ps5, "OFF", 50000, stock: 3, active: false);

        foreach (var (v, i) in new[] { _game, _cheap, _soldOut, _inactive }.Select((v, i) => (v, i)))
        {
            var product = new Product { Title = $"게임{i}", Slug = $"game-{i}" };
            product.Variants.Add(v);
            _db.Products.Add(product);
        }
        _db.Users.Add(new ApplicationUser { Id = UserId, UserName = "tester" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task 비회원이_처음_담으면_새_장바구니가_생기고_Id를_돌려준다()
    {
        var result = await _carts.AddAsync(NoCart, _game.Id, 2);

        Assert.True(result.Success);
        var cartId = Assert.IsType<Guid>(result.CartId);
        var view = await _carts.GetViewAsync(new CartOwner(null, cartId));
        var line = Assert.Single(view.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(80000, view.Subtotal);
    }

    [Fact]
    public async Task 같은_상품을_다시_담으면_수량이_합쳐지고_재고까지만_늘어난다()
    {
        var owner = new CartOwner(UserId, null);
        await _carts.AddAsync(owner, _game.Id, 3);

        var result = await _carts.AddAsync(owner, _game.Id, 4);

        Assert.True(result.Success);
        Assert.Contains("5개까지만", result.Message);
        Assert.Equal(5, Assert.Single((await _carts.GetViewAsync(owner)).Lines).Quantity);

        var full = await _carts.AddAsync(owner, _game.Id, 1);
        Assert.False(full.Success);
    }

    [Fact]
    public async Task 한_상품은_최대_수량을_넘겨_담을_수_없다()
    {
        var owner = new CartOwner(UserId, null);

        Assert.False((await _carts.AddAsync(owner, _cheap.Id, CartService.MaxQuantityPerItem + 1)).Success);
        await _carts.AddAsync(owner, _cheap.Id, CartService.MaxQuantityPerItem);
        var again = await _carts.AddAsync(owner, _cheap.Id, 1);

        Assert.False(again.Success);
        Assert.Equal(CartService.MaxQuantityPerItem, Assert.Single((await _carts.GetViewAsync(owner)).Lines).Quantity);
    }

    [Fact]
    public async Task 품절이거나_판매중지된_상품은_담을_수_없다()
    {
        var owner = new CartOwner(UserId, null);

        Assert.False((await _carts.AddAsync(owner, _soldOut.Id, 1)).Success);
        Assert.False((await _carts.AddAsync(owner, _inactive.Id, 1)).Success);
        Assert.False((await _carts.AddAsync(owner, 99999, 1)).Success);
        Assert.Empty((await _carts.GetViewAsync(owner)).Lines);
    }

    [Fact]
    public async Task 비회원_쿠키로는_회원_장바구니를_볼_수도_고칠_수도_없다()
    {
        var member = new CartOwner(UserId, null);
        var memberCartId = (await _carts.AddAsync(member, _game.Id, 1)).CartId!.Value;
        var itemId = (await _carts.GetViewAsync(member)).Lines[0].ItemId;

        // 회원 장바구니 Id를 비회원 쿠키에 넣은 경우
        var forged = new CartOwner(null, memberCartId);

        Assert.Empty((await _carts.GetViewAsync(forged)).Lines);
        Assert.False((await _carts.RemoveAsync(forged, itemId)).Success);
        Assert.False((await _carts.UpdateQuantityAsync(forged, itemId, 2)).Success);
        Assert.Single((await _carts.GetViewAsync(member)).Lines);
    }

    [Fact]
    public async Task 다른_장바구니의_상품은_수정하거나_뺄_수_없다()
    {
        var a = (await _carts.AddAsync(NoCart, _game.Id, 1)).CartId!.Value;
        var b = (await _carts.AddAsync(NoCart, _cheap.Id, 1)).CartId!.Value;
        var itemOfA = (await _carts.GetViewAsync(new CartOwner(null, a))).Lines[0].ItemId;

        Assert.False((await _carts.RemoveAsync(new CartOwner(null, b), itemOfA)).Success);
        Assert.Single((await _carts.GetViewAsync(new CartOwner(null, a))).Lines);
    }

    [Fact]
    public async Task 수량_변경은_재고를_넘을_수_없다()
    {
        var owner = new CartOwner(UserId, null);
        await _carts.AddAsync(owner, _game.Id, 1);
        var itemId = (await _carts.GetViewAsync(owner)).Lines[0].ItemId;

        Assert.False((await _carts.UpdateQuantityAsync(owner, itemId, 6)).Success);
        Assert.True((await _carts.UpdateQuantityAsync(owner, itemId, 5)).Success);
        Assert.Equal(5, (await _carts.GetViewAsync(owner)).Lines[0].Quantity);
    }

    [Fact]
    public async Task 로그인하면_비회원_장바구니가_회원_장바구니로_합쳐지고_비회원_장바구니는_지워진다()
    {
        var member = new CartOwner(UserId, null);
        await _carts.AddAsync(member, _cheap.Id, 8);

        var guestId = (await _carts.AddAsync(NoCart, _cheap.Id, 5)).CartId!.Value;
        await _carts.AddAsync(new CartOwner(null, guestId), _game.Id, 2);

        await _carts.MergeGuestCartAsync(guestId, UserId);

        var view = await _carts.GetViewAsync(member);
        Assert.Equal(2, view.Lines.Count);
        Assert.Equal(CartService.MaxQuantityPerItem, view.Lines.Single(l => l.VariantId == _cheap.Id).Quantity);
        Assert.Equal(2, view.Lines.Single(l => l.VariantId == _game.Id).Quantity);
        Assert.False(await _db.Carts.AnyAsync(c => c.Id == guestId));
    }

    [Fact]
    public async Task 담은_뒤_품절되거나_판매중지되면_합계에서_빠지고_주문할_수_없다()
    {
        var owner = new CartOwner(UserId, null);
        await _carts.AddAsync(owner, _game.Id, 4);
        await _carts.AddAsync(owner, _cheap.Id, 1);

        await _db.ProductVariants.Where(v => v.Id == _game.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, 2));
        var lowStock = await _carts.GetViewAsync(owner);
        Assert.Equal(CartLineStatus.InsufficientStock, lowStock.Lines.Single(l => l.VariantId == _game.Id).Status);
        Assert.Equal(9000, lowStock.Subtotal);
        Assert.False(lowStock.CanCheckout);

        await _db.ProductVariants.Where(v => v.Id == _game.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsActive, false));
        var inactive = await _carts.GetViewAsync(owner);
        Assert.Equal(CartLineStatus.Unavailable, inactive.Lines.Single(l => l.VariantId == _game.Id).Status);
    }

    [Fact]
    public async Task 오래된_비회원_장바구니만_지운다()
    {
        var clock = new TestClock();
        var carts = new CartService(_db, clock);
        var oldGuest = (await carts.AddAsync(NoCart, _game.Id, 1)).CartId!.Value;
        var member = new CartOwner(UserId, null);
        await carts.AddAsync(member, _game.Id, 1);

        clock.Advance(TimeSpan.FromDays(20));
        var recentGuest = (await carts.AddAsync(NoCart, _cheap.Id, 1)).CartId!.Value;
        clock.Advance(TimeSpan.FromDays(11));     // oldGuest·회원: 31일, recentGuest: 11일

        var removed = await carts.DeleteStaleGuestCartsAsync(TimeSpan.FromDays(30));

        Assert.Equal(1, removed);
        Assert.False(await _db.Carts.AnyAsync(c => c.Id == oldGuest));
        Assert.False(await _db.CartItems.AnyAsync(i => i.CartId == oldGuest));
        Assert.True(await _db.Carts.AnyAsync(c => c.Id == recentGuest));
        Assert.Single((await carts.GetViewAsync(member)).Lines);
    }

    [Fact]
    public async Task 비회원이_장바구니를_쓰면_보관_기간이_다시_시작된다()
    {
        var clock = new TestClock();
        var carts = new CartService(_db, clock);
        var guest = new CartOwner(null, (await carts.AddAsync(NoCart, _cheap.Id, 1)).CartId!.Value);

        clock.Advance(TimeSpan.FromDays(25));
        await carts.AddAsync(guest, _cheap.Id, 1);
        clock.Advance(TimeSpan.FromDays(25));

        Assert.Equal(0, await carts.DeleteStaleGuestCartsAsync(TimeSpan.FromDays(30)));
        Assert.Equal(2, Assert.Single((await carts.GetViewAsync(guest)).Lines).Quantity);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(29_999, CartService.ShippingFee)]
    [InlineData(30_000, 0)]
    public void 배송비는_3만원_미만일때만_붙는다(int subtotal, int expected)
    {
        Assert.Equal(expected, CartService.CalculateShippingFee(subtotal));
    }

    private static ProductVariant Variant(Platform platform, string sku, int price, int stock, bool active = true) =>
        new() { Platform = platform, Sku = sku, ListPrice = price, SalePrice = price, Stock = stock, IsActive = active };
}
