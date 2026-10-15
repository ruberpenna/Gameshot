using GameShot.Web.Data;
using GameShot.Web.Services;
using GameShot.Web.Services.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GameShot.Tests;

/// <summary>서비스 단위 테스트에서 의존성을 한곳에서 조립한다</summary>
public static class TestServices
{
    public static MailQueue MailQueue(ApplicationDbContext db, TimeProvider clock, IMailTransport? transport = null, MailOptions? mail = null) =>
        new(db, transport ?? new FakeMailTransport(), Options.Create(mail ?? new MailOptions()), clock, NullLogger<MailQueue>.Instance);

    public static OrderService Orders(ApplicationDbContext db, TimeProvider clock, ShopOptions? shop = null,
        CartService? carts = null, BenefitService? benefits = null) =>
        new(db,
            carts ?? new CartService(db, clock),
            benefits ?? new BenefitService(db, clock),
            new OrderMailer(db, MailQueue(db, clock), Options.Create(new MailOptions())),
            clock,
            Options.Create(shop ?? new ShopOptions()));
}

/// <summary>보낸 메일을 기억만 하는 가짜 발송기. FailNext만큼 실패한다.</summary>
public sealed class FakeMailTransport : IMailTransport
{
    public List<MimeMessage> Sent { get; } = [];
    public int FailNext { get; set; }

    public Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new InvalidOperationException("SMTP 연결 실패 (테스트)");
        }
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
