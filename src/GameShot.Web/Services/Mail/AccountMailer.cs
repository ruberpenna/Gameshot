using System.Text;
using GameShot.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace GameShot.Web.Services.Mail;

/// <summary>계정 메일: 비밀번호 재설정, 이메일 인증. 링크의 토큰은 Identity가 만들고 1시간 동안 유효하다.</summary>
public class AccountMailer(UserManager<ApplicationUser> users, MailQueue queue, IOptions<MailOptions> options)
{
    private string BaseUrl => options.Value.PublicBaseUrl.TrimEnd('/');

    /// <summary>
    /// 재설정 메일을 보낸다. 가입 여부를 드러내지 않도록 회원이 없어도 조용히 끝내고,
    /// 같은 주소로 방금 보냈으면 다시 보내지 않는다.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user?.Email is null || await queue.RecentlyQueuedAsync(user.Email, MailKinds.PasswordReset, ct))
        {
            return;
        }

        var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
        var link = $"{BaseUrl}/Identity/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        await queue.EnqueueAsync(user.Email, MailKinds.PasswordReset, MailTemplates.PasswordReset(link), ct: ct);
    }

    /// <returns>false면 방금 보내서 이번엔 보내지 않았음</returns>
    public async Task<bool> SendEmailConfirmationAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user.Email is null || user.EmailConfirmed)
        {
            return false;
        }
        if (await queue.RecentlyQueuedAsync(user.Email, MailKinds.EmailConfirm, ct))
        {
            return false;
        }

        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{BaseUrl}/Identity/Account/ConfirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        await queue.EnqueueAsync(user.Email, MailKinds.EmailConfirm,
            MailTemplates.EmailConfirm(user.Nickname ?? user.Email, link), ct: ct);
        return true;
    }

    /// <summary>토큰을 URL에 안전한 문자로 (Identity UI 기본 방식과 같다)</summary>
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? Decode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
