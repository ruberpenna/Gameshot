using GameShot.Web.Data;
using GameShot.Web.Models.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GameShot.Web.Services.Mail;

public static class MailKinds
{
    public const string PasswordReset = "password-reset";
    public const string EmailConfirm = "email-confirm";
    public const string OrderPaid = "order-paid";
    public const string OrderShipped = "order-shipped";
    public const string OrderCancelled = "order-cancelled";
}

/// <summary>메일을 아웃박스에 넣고, 쌓인 메일을 보낸다</summary>
public class MailQueue(
    ApplicationDbContext db,
    IMailTransport transport,
    IOptions<MailOptions> options,
    TimeProvider clock,
    ILogger<MailQueue> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// 메일을 넣는다. 저장은 호출한 쪽의 SaveChanges(·트랜잭션)에 맡기려면 save: false.
    /// </summary>
    public async Task EnqueueAsync(string to, string kind, MailContent content, bool save = true, CancellationToken ct = default)
    {
        db.EmailMessages.Add(new EmailMessage
        {
            ToAddress = to,
            Kind = kind,
            Subject = content.Subject,
            HtmlBody = content.Html,
            TextBody = content.Text,
            CreatedAt = Now,
            NextAttemptAt = Now,
        });
        if (save)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>같은 주소로 같은 종류의 메일을 방금 넣었는지 (재설정·인증 메일 연타 방지)</summary>
    public Task<bool> RecentlyQueuedAsync(string to, string kind, CancellationToken ct = default)
    {
        var since = Now - options.Value.ResendCooldown;
        return db.EmailMessages.AnyAsync(m => m.ToAddress == to && m.Kind == kind && m.CreatedAt > since, ct);
    }

    /// <summary>보낼 차례인 메일을 보낸다. 실패하면 1, 4, 9, 16분 뒤 다시, 5번 실패하면 포기.</summary>
    /// <returns>보낸 메일 수</returns>
    public async Task<int> SendPendingAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var now = Now;
        var pending = await db.EmailMessages
            .Where(m => m.SentAt == null && m.Attempts < MailOptions.MaxAttempts && m.NextAttemptAt <= now)
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var mail in pending)
        {
            mail.Attempts++;
            try
            {
                await transport.SendAsync(ToMime(mail), ct);
                mail.SentAt = Now;
                mail.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                mail.NextAttemptAt = Now + TimeSpan.FromMinutes(mail.Attempts * mail.Attempts);
                logger.LogWarning(ex, "메일 발송 실패 ({Attempts}회째): {Id} {Kind}", mail.Attempts, mail.Id, mail.Kind);
            }
            // 한 통 보낼 때마다 저장해서, 도중에 꺼져도 이미 보낸 메일을 또 보내지 않는다
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private MimeMessage ToMime(EmailMessage mail)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(MailboxAddress.Parse(mail.ToAddress));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { HtmlBody = mail.HtmlBody, TextBody = mail.TextBody }.ToMessageBody();
        return message;
    }
}

/// <summary>주기적으로 아웃박스를 비운다</summary>
public class MailWorker(IServiceScopeFactory scopes, IOptions<MailOptions> options, TimeProvider clock, ILogger<MailWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.WorkerInterval, clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<MailQueue>().SendPendingAsync(ct: stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("메일 {Count}통을 보냈습니다.", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "메일 발송 작업이 실패했습니다.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
