using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Benefits;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameShot.Tests;

public sealed class BenefitTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestClock _clock = new();
    private ApplicationDbContext _db = null!;
    private CartService _carts = null!;
    private BenefitService _benefits = null!;
    private OrderService _orders = null!;

    private ProductVariant _game = null!;   // 40,000원 / 재고 10
    private ProductVariant _cheap = null!;  // 10,000원 / 재고 10
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
        _carts = new CartService(_db, _clock);
        _benefits = new BenefitService(_db, _clock);
        _orders = TestServices.Orders(_db, _clock, carts: _carts, benefits: _benefits);

        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        _game = new ProductVariant { Platform = ps5, Sku = "GAME", ListPrice = 40000, SalePrice = 40000, Stock = 10 };
        _cheap = new ProductVariant { Platform = ps5, Sku = "CHEAP", ListPrice = 10000, SalePrice = 10000, Stock = 10 };
        var game = new Product { Title = "게임", Slug = "game" };
        game.Variants.Add(_game);
        var cheap = new Product { Title = "싼 게임", Slug = "cheap" };
        cheap.Variants.Add(_cheap);
        _db.Products.AddRange(game, cheap);
        _db.Users.AddRange(
            new ApplicationUser { Id = Alice, UserName = "alice", Email = "alice@example.test", NormalizedEmail = "ALICE@EXAMPLE.TEST" },
            new ApplicationUser { Id = Bob, UserName = "bob", Email = "bob@example.test", NormalizedEmail = "BOB@EXAMPLE.TEST" });
        _db.Coupons.AddRange(
            new Coupon { Code = "WON5000", Name = "5천원 할인", DiscountType = DiscountType.Amount, DiscountValue = 5000, MinOrderAmount = 30000 },
            new Coupon { Code = "PCT10", Name = "10% 할인", DiscountType = DiscountType.Percent, DiscountValue = 10, MaxDiscount = 3000 },
            new Coupon { Code = "LIMIT1", Name = "선착순 1명", DiscountType = DiscountType.Amount, DiscountValue = 1000, IssueLimit = 1 },
            new Coupon { Code = "OFF", Name = "중지된 쿠폰", DiscountType = DiscountType.Amount, DiscountValue = 1000, IsActive = false },
            new Coupon
            {
                Code = "SOON", Name = "기간 한정", DiscountType = DiscountType.Amount, DiscountValue = 1000,
                EndsAt = _clock.Now.UtcDateTime.AddDays(1),
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---- 계산 규칙 ----

    [Theory]
    [InlineData(DiscountType.Amount, 5000, null, 0, 40000, 5000)]
    [InlineData(DiscountType.Amount, 5000, null, 0, 3000, 3000)]       // 상품 금액보다 많이 깎지 않는다
    [InlineData(DiscountType.Amount, 5000, null, 30000, 29999, 0)]     // 최소 주문 금액 미만
    [InlineData(DiscountType.Percent, 10, null, 0, 12345, 1230)]       // 1234.5 → 10원 단위 버림
    [InlineData(DiscountType.Percent, 10, 3000, 0, 50000, 3000)]       // 최대 할인액
    [InlineData(DiscountType.Percent, 100, null, 0, 20000, 20000)]
    public void 쿠폰_할인액_계산(DiscountType type, int value, int? max, int min, int subtotal, int expected)
    {
        var coupon = new Coupon { Code = "X", Name = "x", DiscountType = type, DiscountValue = value, MaxDiscount = max, MinOrderAmount = min };
        Assert.Equal(expected, PriceCalculator.CouponDiscount(coupon, subtotal));
    }

    [Fact]
    public void 포인트는_상품금액에서_쿠폰을_뺀_만큼만_쓸_수_있고_배송비는_못_낸다()
    {
        Assert.Equal(7000, PriceCalculator.MaxUsablePoints(subtotal: 10000, couponDiscount: 3000, balance: 50000));
        Assert.Equal(2000, PriceCalculator.MaxUsablePoints(subtotal: 10000, couponDiscount: 3000, balance: 2000));
        Assert.Equal(0, PriceCalculator.MaxUsablePoints(subtotal: 10000, couponDiscount: 10000, balance: 2000));
        Assert.Equal(399, PriceCalculator.EarnedPoints(39999, 0.01m));
    }

    // ---- 쿠폰함 ----

    [Fact]
    public async Task 쿠폰_코드는_대소문자_구분없이_한_번만_등록된다()
    {
        Assert.True((await _benefits.RegisterCodeAsync(Alice, " won5000 ")).Success);
        Assert.False((await _benefits.RegisterCodeAsync(Alice, "WON5000")).Success);
        Assert.False((await _benefits.RegisterCodeAsync(Alice, "NOPE")).Success);
        Assert.False((await _benefits.RegisterCodeAsync(Alice, "OFF")).Success);

        var wallet = await _benefits.GetWalletAsync(Alice);
        Assert.Equal("5천원 할인", Assert.Single(wallet).Name);
    }

    [Fact]
    public async Task 선착순_쿠폰은_제한_수량까지만_발급된다()
    {
        Assert.True((await _benefits.IssueToEmailAsync(await CouponId("LIMIT1"), "alice@example.test")).Success);
        Assert.False((await _benefits.RegisterCodeAsync(Bob, "LIMIT1")).Success);
        Assert.False((await _benefits.IssueToEmailAsync(await CouponId("LIMIT1"), "nobody@example.test")).Success);
    }

    [Fact]
    public async Task 기간이_끝난_쿠폰은_주문서에_나오지_않는다()
    {
        await _benefits.RegisterCodeAsync(Alice, "SOON");
        Assert.Single(await _benefits.GetOptionsAsync(Alice, 40000));

        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Empty(await _benefits.GetOptionsAsync(Alice, 40000));
        Assert.False(Assert.Single(await _benefits.GetWalletAsync(Alice)).IsUsableNow);
    }

    [Fact]
    public async Task 주문서는_최소금액에_못_미치는_쿠폰을_이유와_함께_보여준다()
    {
        await _benefits.RegisterCodeAsync(Alice, "WON5000");
        await _benefits.RegisterCodeAsync(Alice, "PCT10");

        var options = await _benefits.GetOptionsAsync(Alice, 10000);

        var pct = options.Single(o => o.Name == "10% 할인");
        Assert.True(pct.IsApplicable);
        Assert.Equal(1000, pct.Discount);
        var won = options.Single(o => o.Name == "5천원 할인");
        Assert.False(won.IsApplicable);
        Assert.Contains("30,000원 이상", won.UnavailableReason);
    }

    // ---- 주문에 적용 ----

    [Fact]
    public async Task 쿠폰과_포인트로_주문하면_금액이_줄고_쿠폰과_포인트가_사용된다()
    {
        await _benefits.RegisterCodeAsync(Alice, "WON5000");
        await _benefits.AdjustPointsAsync(Alice, 3000, "테스트 지급");
        await AddToCart(Alice, _game, 1);
        var couponId = await WalletId(Alice, "5천원 할인");

        var quote = await _orders.QuoteAsync(Alice, couponId, 2000);
        Assert.Null(quote.Error);
        Assert.Equal((40000, 0, 5000, 2000, 33000), (quote.Price.Subtotal, quote.Price.ShippingFee, quote.Price.CouponDiscount, quote.Price.PointsUsed, quote.Price.Total));

        var result = await _orders.PlaceOrderAsync(Alice, Home, 33000, couponId, 2000);

        Assert.True(result.Success, result.Message);
        var order = await _orders.FindForUserAsync(Alice, result.OrderNumber!);
        Assert.Equal((5000, "5천원 할인", 2000, 33000), (order!.CouponDiscount, order.CouponName, order.PointsUsed, order.Total));
        Assert.Equal(1000, await _benefits.GetBalanceAsync(Alice));
        Assert.NotNull(Assert.Single(await _benefits.GetWalletAsync(Alice)).UsedAt);
        Assert.Empty(await _benefits.GetOptionsAsync(Alice, 40000));

        var use = (await _benefits.GetHistoryAsync(Alice)).First();
        Assert.Equal((PointType.Use, -2000, 1000), (use.Type, use.Amount, use.BalanceAfter));
    }

    [Fact]
    public async Task 쓴_쿠폰은_다른_주문에_다시_쓸_수_없다()
    {
        await _benefits.RegisterCodeAsync(Alice, "PCT10");
        var couponId = await WalletId(Alice, "10% 할인");
        await AddToCart(Alice, _cheap, 1);
        Assert.True((await _orders.PlaceOrderAsync(Alice, Home, 10000 + CartService.ShippingFee - 1000, couponId)).Success);

        await AddToCart(Alice, _cheap, 1);
        var again = await _orders.PlaceOrderAsync(Alice, Home, 10000 + CartService.ShippingFee - 1000, couponId);

        Assert.False(again.Success);
        Assert.Equal(9, await StockOf(_cheap));   // 두 번째 주문은 재고도 건드리지 않았다
    }

    [Fact]
    public async Task 남의_쿠폰이나_잔액보다_많은_포인트로는_주문할_수_없다()
    {
        await _benefits.RegisterCodeAsync(Bob, "PCT10");
        var bobsCoupon = await WalletId(Bob, "10% 할인");
        await _benefits.AdjustPointsAsync(Alice, 1000, null);
        await AddToCart(Alice, _game, 1);

        Assert.False((await _orders.PlaceOrderAsync(Alice, Home, 36000, bobsCoupon)).Success);
        Assert.False((await _orders.PlaceOrderAsync(Alice, Home, 38000, null, 2000)).Success);
        Assert.False((await _orders.PlaceOrderAsync(Alice, Home, 40000, null, -500)).Success);
        Assert.Empty(_db.Orders);
        Assert.Equal(1000, await _benefits.GetBalanceAsync(Alice));
    }

    [Fact]
    public async Task 미리보기는_쓸_수_있는_만큼으로_맞추고_이유를_알려준다()
    {
        await _benefits.AdjustPointsAsync(Alice, 50000, null);
        await AddToCart(Alice, _cheap, 1);

        var quote = await _orders.QuoteAsync(Alice, null, 99999);

        Assert.Equal(10000, quote.Price.PointsUsed);   // 상품 금액까지만
        Assert.Equal(CartService.ShippingFee, quote.Price.Total);
        Assert.NotNull(quote.Error);

        var wrongCoupon = await _orders.QuoteAsync(Alice, 12345, 0);
        Assert.Null(wrongCoupon.AppliedUserCouponId);
        Assert.NotNull(wrongCoupon.Error);
    }

    [Fact]
    public async Task 주문을_취소하면_쿠폰과_포인트가_돌아온다()
    {
        await _benefits.RegisterCodeAsync(Alice, "WON5000");
        await _benefits.AdjustPointsAsync(Alice, 3000, null);
        await AddToCart(Alice, _game, 1);
        var couponId = await WalletId(Alice, "5천원 할인");
        var no = (await _orders.PlaceOrderAsync(Alice, Home, 32000, couponId, 3000)).OrderNumber!;
        await _orders.CompletePaymentAsync(Alice, no);

        Assert.True((await _orders.CancelAsync(Alice, no)).Success);

        Assert.Equal(3000, await _benefits.GetBalanceAsync(Alice));
        var coupon = Assert.Single(await _benefits.GetWalletAsync(Alice));
        Assert.Null(coupon.UsedAt);
        Assert.Equal(PointType.Refund, (await _benefits.GetHistoryAsync(Alice)).First().Type);

        // 돌려받은 쿠폰은 다시 쓸 수 있다
        await AddToCart(Alice, _game, 1);
        Assert.True((await _orders.PlaceOrderAsync(Alice, Home, 35000, couponId)).Success);
    }

    [Fact]
    public async Task 결제_시간이_지나_취소돼도_포인트가_돌아온다()
    {
        await _benefits.AdjustPointsAsync(Alice, 5000, null);
        await AddToCart(Alice, _game, 1);
        await _orders.PlaceOrderAsync(Alice, Home, 35000, null, 5000);
        Assert.Equal(0, await _benefits.GetBalanceAsync(Alice));

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await _orders.ExpirePendingOrdersAsync());

        Assert.Equal(5000, await _benefits.GetBalanceAsync(Alice));
    }

    [Fact]
    public async Task 배송이_완료되면_배송비를_뺀_실결제액의_1퍼센트를_적립한다()
    {
        await _benefits.RegisterCodeAsync(Alice, "PCT10");
        await AddToCart(Alice, _cheap, 2);   // 20,000원, 배송비 3,000원
        var couponId = await WalletId(Alice, "10% 할인");
        var no = (await _orders.PlaceOrderAsync(Alice, Home, 20000 + CartService.ShippingFee - 2000, couponId)).OrderNumber!;
        await _orders.CompletePaymentAsync(Alice, no);
        await _orders.StartPreparingAsync(no);
        await _orders.ShipAsync(no, "CJ대한통운", "1");

        var delivered = await _orders.MarkDeliveredAsync(no);

        Assert.True(delivered.Success);
        Assert.Contains("180P", delivered.Message);   // (20,000 - 2,000) × 1%
        Assert.Equal(180, await _benefits.GetBalanceAsync(Alice));
        Assert.Equal(180, (await _orders.FindForUserAsync(Alice, no))!.PointsEarned);
        Assert.False((await _orders.MarkDeliveredAsync(no)).Success);
        Assert.Equal(180, await _benefits.GetBalanceAsync(Alice));
    }

    [Fact]
    public async Task 관리자는_잔액보다_많이_차감할_수_없다()
    {
        Assert.True((await _benefits.AdjustPointsAsync(Alice, 1000, "이벤트")).Success);
        Assert.False((await _benefits.AdjustPointsAsync(Alice, -1001, null)).Success);
        Assert.True((await _benefits.AdjustPointsAsync(Alice, -1000, null)).Success);
        Assert.False((await _benefits.AdjustPointsAsync(Alice, 0, null)).Success);

        Assert.Equal(0, await _benefits.GetBalanceAsync(Alice));
        Assert.Equal([0, 1000], (await _benefits.GetHistoryAsync(Alice)).Select(h => h.BalanceAfter));
    }

    private Task AddToCart(string userId, ProductVariant v, int quantity) =>
        _carts.AddAsync(new CartOwner(userId, null), v.Id, quantity);

    private Task<int> CouponId(string code) => _db.Coupons.Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    private async Task<int> WalletId(string userId, string name) =>
        (await _benefits.GetWalletAsync(userId)).Single(c => c.Name == name).UserCouponId;

    private Task<int> StockOf(ProductVariant v) =>
        _db.ProductVariants.Where(x => x.Id == v.Id).Select(x => x.Stock).SingleAsync();
}
