using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GameShot.Web.Services.Mail;

/// <summary>메일 한 통을 실제로 내보낸다 (실패하면 예외)</summary>
public interface IMailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}

/// <summary>SMTP 발송 (네이버: smtp.naver.com 465 SSL, 계정은 네이버 아이디·비밀번호 또는 앱 비밀번호)</summary>
public class SmtpMailTransport(IOptions<MailOptions> options) : IMailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var host = o.SmtpHost ?? throw new InvalidOperationException("Mail:SmtpHost 설정이 없습니다.");
        using var client = new SmtpClient();
        client.Timeout = 30_000;
        await client.ConnectAsync(host, o.SmtpPort,
            o.SmtpSslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrEmpty(o.SmtpUserName))
        {
            await client.AuthenticateAsync(o.SmtpUserName, o.SmtpPassword ?? "", ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>개발용: 보내는 대신 .eml 파일로 저장 (메일 프로그램이나 브라우저로 열어 볼 수 있다)</summary>
public class PickupDirectoryMailTransport(IOptions<MailOptions> options, IWebHostEnvironment env) : IMailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var dir = Path.Combine(env.ContentRootPath, options.Value.PickupDirectory);
        Directory.CreateDirectory(dir);
        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml";
        await message.WriteToAsync(Path.Combine(dir, name), ct);
    }
}
