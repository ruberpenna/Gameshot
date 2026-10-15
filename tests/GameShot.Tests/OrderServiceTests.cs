using System.Text.RegularExpressions;
using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameShot.Tests;

public sealed class OrderServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ApplicationDbContext _db = null!;
    private CartService _carts = null!;
    private OrderService _orders = null!;
    private readonly TestClock _clock = new();
    private readonly ShopOptions _options = new() { PaymentTimeout = TimeSpan.FromMinutes(30) };

    private ProductVariant _game = null!;   // 40,000원 / 재고 5
    private ProductVariant _last = null!;   // 20,000원 / 재고 1
    private const string Alice = "alice";
    private const string Bob = "bob";

    private static readonly ShippingInfo Home = new()
    {
        RecipientName = "홍길동",
        Phone = "010-1234-5678",
        PostalCode = "06236",
        Address1 = "서울 강남구 테헤란로 1",
        Address2 = "101호",
    };

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _carts = new CartService(_db, _clock);
        _orders = new OrderService(_db, _carts, _clock, Options.Create(_options));

        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        _game = new ProductVariant { Platform = ps5, Sku = "GAME", ListPrice = 45000, SalePrice = 40000, Stock = 5, Edition = "디럭스" };
        _last = new ProductVariant { Platform = ps5, Sku = "LAST", ListPrice = 20000, SalePrice = 20000, Stock = 1 };
        var game = new Product { Title = "게임", Slug = "game", ThumbnailPath = "/images/game.jpg" };
        game.Variants.Add(_game);
        var last = new Product { Title = "마지막 하나", Slug = "last" };
        last.Variants.Add(_last);
        _db.Products.AddRange(game, last);
        _db.Users.AddRange(new ApplicationUser { Id = Alice, UserName = "alice" }, new ApplicationUser { Id = Bob, UserName = "bob" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task 주문하면_재고가_줄고_장바구니가_비고_주문시점_정보가_저장된다()
    {
        await AddToCart(Alice, _game, 2);

        var result = await PlaceOrder(Alice);

        Assert.True(result.Success, result.Message);
        Assert.Matches(new Regex(@"^GS\d{8}-[A-Z2-9]{6}$"), result.OrderNumber);
        Assert.Equal(3, await StockOf(_game));
        Assert.Empty((await _carts.GetViewAsync(new CartOwner(Alice, null))).Lines);

        var order = await _orders.FindForUserAsync(Alice, result.OrderNumber!);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(80000, order.Subtotal);
        Assert.Equal(0, order.ShippingFee);
        Assert.Equal(80000, order.Total);
        Assert.Equal("홍길동", order.Shipping.RecipientName);
        var item = Assert.Single(order.Items);
        Assert.Equal(("게임", "PlayStation 5", "디럭스", 40000, 2, 80000),
            (item.ProductTitle, item.PlatformName, item.Edition, item.UnitPrice, item.Quantity, item.LineTotal));
    }

    [Fact]
    public async Task 주문_후_상품_가격이_바뀌어도_주문_내역은_그대로다()
    {
        await AddToCart(Alice, _game, 1);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;

        await _db.ProductVariants.Where(v => v.Id == _game.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.SalePrice, 1000));

        var order = await _orders.FindForUserAsync(Alice, orderNumber);
        Assert.Equal(40000, order!.Items.Single().UnitPrice);
        Assert.Equal(40000, order.Total);
    }

    [Fact]
    public async Task 빈_장바구니는_주문할_수_없다()
    {
        var result = await _orders.PlaceOrderAsync(Alice, Home, 0);

        Assert.False(result.Success);
        Assert.Empty(_db.Orders);
    }

    [Fact]
    public async Task 화면에_보여준_금액과_다르면_주문하지_않고_재고도_그대로다()
    {
        await AddToCart(Alice, _game, 1);

        var result = await _orders.PlaceOrderAsync(Alice, Home, expectedTotal: 12345);

        Assert.False(result.Success);
        Assert.Contains("가격이 바뀌었습니다", result.Message);
        Assert.Equal(5, await StockOf(_game));
        Assert.Empty(_db.Orders);
    }

    [Fact]
    public async Task 마지막_재고는_먼저_주문한_사람만_가져간다()
    {
        await AddToCart(Alice, _last, 1);
        await AddToCart(Bob, _last, 1);

        Assert.True((await PlaceOrder(Alice)).Success);
        var bob = await PlaceOrder(Bob);

        Assert.False(bob.Success);
        Assert.Equal(0, await StockOf(_last));
        Assert.Single(_db.Orders);
    }

    [Fact]
    public async Task 가상결제_성공은_한_번만_처리된다()
    {
        await AddToCart(Alice, _game, 1);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;

        Assert.True((await _orders.CompletePaymentAsync(Alice, orderNumber)).Success);
        Assert.False((await _orders.CompletePaymentAsync(Alice, orderNumber)).Success);

        var order = await _orders.FindForUserAsync(Alice, orderNumber);
        Assert.Equal(OrderStatus.Paid, order!.Status);
        Assert.NotNull(order.PaidAt);
    }

    [Fact]
    public async Task 가상결제_실패면_주문이_취소되고_재고가_돌아온다()
    {
        await AddToCart(Alice, _game, 3);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;
        Assert.Equal(2, await StockOf(_game));

        Assert.True((await _orders.FailPaymentAsync(Alice, orderNumber)).Success);

        Assert.Equal(5, await StockOf(_game));
        Assert.Equal(OrderStatus.Cancelled, (await _orders.FindForUserAsync(Alice, orderNumber))!.Status);
    }

    [Fact]
    public async Task 결제완료_주문을_취소하면_재고가_한_번만_돌아온다()
    {
        await AddToCart(Alice, _game, 2);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;
        await _orders.CompletePaymentAsync(Alice, orderNumber);

        Assert.True((await _orders.CancelAsync(Alice, orderNumber)).Success);
        Assert.False((await _orders.CancelAsync(Alice, orderNumber)).Success);
        Assert.False((await _orders.CompletePaymentAsync(Alice, orderNumber)).Success);

        Assert.Equal(5, await StockOf(_game));
        var order = await _orders.FindForUserAsync(Alice, orderNumber);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public async Task 배송이_시작된_주문은_취소할_수_없다()
    {
        await AddToCart(Alice, _game, 1);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;
        await _db.Orders.Where(o => o.OrderNumber == orderNumber).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Shipped));

        Assert.False((await _orders.CancelAsync(Alice, orderNumber)).Success);
        Assert.Equal(4, await StockOf(_game));
    }

    [Fact]
    public async Task 다른_사람의_주문은_보거나_결제하거나_취소할_수_없다()
    {
        await AddToCart(Alice, _game, 1);
        var orderNumber = (await PlaceOrder(Alice)).OrderNumber!;

        Assert.Null(await _orders.FindForUserAsync(Bob, orderNumber));
        Assert.False((await _orders.CompletePaymentAsync(Bob, orderNumber)).Success);
        Assert.False((await _orders.CancelAsync(Bob, orderNumber)).Success);
        Assert.Empty(await _orders.ListForUserAsync(Bob));
        Assert.Equal(OrderStatus.PendingPayment, (await _orders.FindForUserAsync(Alice, orderNumber))!.Status);
    }

    [Fact]
    public async Task 주문_목록과_최근_배송지()
    {
        Assert.Null(await _orders.GetLastShippingAsync(Alice));
        await AddToCart(Alice, _game, 1);
        await AddToCart(Alice, _last, 1);
        await PlaceOrder(Alice);

        var summary = Assert.Single(await _orders.ListForUserAsync(Alice));
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal("게임", summary.FirstItemTitle);
        Assert.Equal("서울 강남구 테헤란로 1", (await _orders.GetLastShippingAsync(Alice))!.Address1);
    }

    // ---- 결제 기한 ----

    [Fact]
    public async Task 결제_기한이_지난_주문만_취소되고_재고가_돌아온다()
    {
        await AddToCart(Alice, _game, 2);
        var old = (await PlaceOrder(Alice)).OrderNumber!;
        _clock.Advance(TimeSpan.FromMinutes(20));
        await AddToCart(Alice, _game, 1);
        var recent = (await PlaceOrder(Alice)).OrderNumber!;
        Assert.Equal(2, await StockOf(_game));

        _clock.Advance(TimeSpan.FromMinutes(11));   // old: 31분 경과, recent: 11분 경과
        var expired = await _orders.ExpirePendingOrdersAsync();

        Assert.Equal(1, expired);
        var oldOrder = await _orders.FindForUserAsync(Alice, old);
        Assert.Equal(OrderStatus.Cancelled, oldOrder!.Status);
        Assert.Equal(OrderService.ReasonPaymentTimeout, oldOrder.CancelReason);
        Assert.Equal(OrderStatus.PendingPayment, (await _orders.FindForUserAsync(Alice, recent))!.Status);
        Assert.Equal(4, await StockOf(_game));

        // 다시 돌려도 이미 처리한 주문은 건드리지 않는다
        Assert.Equal(0, await _orders.ExpirePendingOrdersAsync());
        Assert.Equal(4, await StockOf(_game));
    }

    [Fact]
    public async Task 결제를_마친_주문은_기한이_지나도_취소되지_않는다()
    {
        await AddToCart(Alice, _game, 1);
        var no = (await PlaceOrder(Alice)).OrderNumber!;
        await _orders.CompletePaymentAsync(Alice, no);

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(0, await _orders.ExpirePendingOrdersAsync());
        Assert.Equal(OrderStatus.Paid, (await _orders.FindForUserAsync(Alice, no))!.Status);
        Assert.Equal(4, await StockOf(_game));
    }

    [Fact]
    public async Task 기한이_지난_뒤_결제하면_거부하고_그_자리에서_취소한다()
    {
        await AddToCart(Alice, _game, 1);
        var no = (await PlaceOrder(Alice)).OrderNumber!;
        _clock.Advance(TimeSpan.FromMinutes(31));

        // 정리 작업이 아직 안 돌았어도 결제는 막혀야 한다
        var pay = await _orders.CompletePaymentAsync(Alice, no);

        Assert.False(pay.Success);
        Assert.Contains("결제 시간이 지나", pay.Message);
        var order = await _orders.FindForUserAsync(Alice, no);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Equal(OrderService.ReasonPaymentTimeout, order.CancelReason);
        Assert.Equal(5, await StockOf(_game));
    }

    [Fact]
    public async Task 기한_직전에는_결제할_수_있고_취소_사유가_종류별로_남는다()
    {
        await AddToCart(Alice, _game, 1);
        var paid = (await PlaceOrder(Alice)).OrderNumber!;
        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True((await _orders.CompletePaymentAsync(Alice, paid)).Success);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(-29).AddMinutes(30),
            _orders.PaymentDeadline((await _orders.FindForUserAsync(Alice, paid))!));

        await AddToCart(Alice, _game, 1);
        var failed = (await PlaceOrder(Alice)).OrderNumber!;
        await _orders.FailPaymentAsync(Alice, failed);
        Assert.Equal(OrderService.ReasonPaymentFailed, (await _orders.FindForUserAsync(Alice, failed))!.CancelReason);

        await _orders.CancelAsync(Alice, paid);
        Assert.Equal(OrderService.ReasonCustomer, (await _orders.FindForUserAsync(Alice, paid))!.CancelReason);
    }

    [Theory]
    [InlineData("01012345678", "010-1234-5678")]
    [InlineData("010-1234-5678", "010-1234-5678")]
    [InlineData("0212345678", "02-1234-5678")]
    [InlineData("0311234567", "031-123-4567")]
    public void 연락처는_하이픈_형식으로_맞춘다(string input, string expected)
    {
        Assert.Equal(expected, CheckoutForm.NormalizePhone(input));
    }

    private Task AddToCart(string userId, ProductVariant variant, int quantity) =>
        _carts.AddAsync(new CartOwner(userId, null), variant.Id, quantity);

    private async Task<OrderResult> PlaceOrder(string userId)
    {
        var total = (await _carts.GetViewAsync(new CartOwner(userId, null))).Total;
        return await _orders.PlaceOrderAsync(userId, Home, total);
    }

    private Task<int> StockOf(ProductVariant variant) =>
        _db.ProductVariants.Where(v => v.Id == variant.Id).Select(v => v.Stock).SingleAsync();
}
