using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;
using GameShot.Web.Services.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Tests;

public sealed class MailTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestClock _clock = new();
    private ApplicationDbContext _db = null!;
    private const string Alice = "alice";

    private static readonly ShippingInfo Home = new()
    {
        RecipientName = "<b>홍길동</b>", Phone = "010-1234-5678", PostalCode = "06236", Address1 = "서울 강남구 테헤란로 1",
    };

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        var ps5 = new Platform { Code = "ps5", Name = "PlayStation 5" };
        var product = new Product { Title = "게임", Slug = "game" };
        product.Variants.Add(new ProductVariant { Platform = ps5, Sku = "GAME", ListPrice = 40000, SalePrice = 40000, Stock = 10 });
        _db.Products.Add(product);
        _db.Users.Add(new ApplicationUser { Id = Alice, UserName = "alice", Email = "alice@example.test" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task 쌓인_메일을_보내고_보낸_메일은_다시_보내지_않는다()
    {
        var transport = new FakeMailTransport();
        var queue = TestServices.MailQueue(_db, _clock, transport, new MailOptions { FromAddress = "shop@example.test", FromName = "GameShot" });
        await queue.EnqueueAsync("a@example.test", MailKinds.OrderPaid, new MailContent("제목", "<p>본문</p>", "본문"));

        Assert.Equal(1, await queue.SendPendingAsync());
        Assert.Equal(0, await queue.SendPendingAsync());

        var sent = Assert.Single(transport.Sent);
        Assert.Equal("제목", sent.Subject);
        Assert.Equal("shop@example.test", sent.From.Mailboxes.Single().Address);
        Assert.Contains("<p>본문</p>", sent.HtmlBody);
        Assert.NotNull((await _db.EmailMessages.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task 실패하면_점점_늦게_다시_시도하고_5번_실패하면_포기한다()
    {
        var transport = new FakeMailTransport { FailNext = 99 };
        var queue = TestServices.MailQueue(_db, _clock, transport);
        await queue.EnqueueAsync("a@example.test", MailKinds.OrderPaid, new MailContent("s", "h", "t"));

        Assert.Equal(0, await queue.SendPendingAsync());
        var mail = await _db.EmailMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, mail.Attempts);
        Assert.Contains("SMTP 연결 실패", mail.LastError);

        // 1분이 지나기 전에는 다시 시도하지 않는다
        await queue.SendPendingAsync();
        Assert.Equal(1, (await _db.EmailMessages.AsNoTracking().SingleAsync()).Attempts);

        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            await queue.SendPendingAsync();
        }
        Assert.Equal(MailOptions.MaxAttempts, (await _db.EmailMessages.AsNoTracking().SingleAsync()).Attempts);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task 일시적으로_실패해도_다음_시도에_보낸다()
    {
        var transport = new FakeMailTransport { FailNext = 1 };
        var queue = TestServices.MailQueue(_db, _clock, transport);
        await queue.EnqueueAsync("a@example.test", MailKinds.OrderPaid, new MailContent("s", "h", "t"));

        await queue.SendPendingAsync();
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, await queue.SendPendingAsync());
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task 같은_주소_같은_종류는_잠시_동안_다시_넣지_않는다()
    {
        var queue = TestServices.MailQueue(_db, _clock);
        await queue.EnqueueAsync("a@example.test", MailKinds.PasswordReset, new MailContent("s", "h", "t"));

        Assert.True(await queue.RecentlyQueuedAsync("a@example.test", MailKinds.PasswordReset));
        Assert.False(await queue.RecentlyQueuedAsync("a@example.test", MailKinds.EmailConfirm));
        Assert.False(await queue.RecentlyQueuedAsync("b@example.test", MailKinds.PasswordReset));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(await queue.RecentlyQueuedAsync("a@example.test", MailKinds.PasswordReset));
    }

    [Fact]
    public async Task 주문_결제_발송_취소_때마다_고객에게_메일이_쌓인다()
    {
        var orders = TestServices.Orders(_db, _clock);
        var carts = new CartService(_db, _clock);
        var variantId = await _db.ProductVariants.Select(v => v.Id).SingleAsync();

        await carts.AddAsync(new CartOwner(Alice, null), variantId, 1);
        var no = (await orders.PlaceOrderAsync(Alice, Home, 40000)).OrderNumber!;
        Assert.Empty(_db.EmailMessages);   // 주문서 제출(결제 대기)에는 메일이 없다

        await orders.CompletePaymentAsync(Alice, no);
        await orders.StartPreparingAsync(no);
        await orders.ShipAsync(no, "CJ대한통운", "1234567890");

        await carts.AddAsync(new CartOwner(Alice, null), variantId, 1);
        var cancelled = (await orders.PlaceOrderAsync(Alice, Home, 40000)).OrderNumber!;
        await orders.FailPaymentAsync(Alice, cancelled);

        var mails = await _db.EmailMessages.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal([MailKinds.OrderPaid, MailKinds.OrderShipped, MailKinds.OrderCancelled], mails.Select(m => m.Kind));
        Assert.All(mails, m => Assert.Equal("alice@example.test", m.ToAddress));
        Assert.Contains(no, mails[0].Subject);
        Assert.Contains("1234567890", mails[1].HtmlBody);
        Assert.Contains("결제 실패", mails[2].HtmlBody);
        Assert.Contains($"/orders/{cancelled}", mails[2].HtmlBody);
        // 사용자가 입력한 값은 HTML로 해석되지 않게 인코딩한다
        Assert.DoesNotContain("<b>홍길동</b>", mails[0].HtmlBody);
        Assert.Contains("&lt;b&gt;", mails[0].HtmlBody);
    }

    [Fact]
    public async Task 결제_시간이_지나_자동_취소돼도_안내_메일이_간다()
    {
        var orders = TestServices.Orders(_db, _clock);
        var carts = new CartService(_db, _clock);
        await carts.AddAsync(new CartOwner(Alice, null), await _db.ProductVariants.Select(v => v.Id).SingleAsync(), 1);
        await orders.PlaceOrderAsync(Alice, Home, 40000);

        _clock.Advance(TimeSpan.FromHours(1));
        await orders.ExpirePendingOrdersAsync();

        var mail = await _db.EmailMessages.SingleAsync();
        Assert.Equal(MailKinds.OrderCancelled, mail.Kind);
        Assert.Contains(OrderService.ReasonPaymentTimeout, mail.HtmlBody);
    }
}
