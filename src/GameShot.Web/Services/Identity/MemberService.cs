using System.Text.RegularExpressions;
using GameShot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services.Identity;

/// <summary>회원 프로필 규칙 (닉네임)</summary>
public partial class MemberService(ApplicationDbContext db)
{
    public const int NicknameMinLength = 2;
    public const int NicknameMaxLength = 12;

    /// <summary>한글·영문·숫자, 2~12자</summary>
    [GeneratedRegex(@"^[가-힣A-Za-z0-9]{2,12}$")]
    private static partial Regex NicknamePattern();

    /// <summary>운영자로 오해할 수 있는 닉네임</summary>
    private static readonly string[] Reserved = ["관리자", "운영자", "admin", "gameshot", "게임샷"];

    /// <returns>쓸 수 없으면 이유, 쓸 수 있으면 null</returns>
    public async Task<string?> ValidateNicknameAsync(string? nickname, string? exceptUserId, CancellationToken ct = default)
    {
        nickname = nickname?.Trim();
        if (string.IsNullOrEmpty(nickname))
        {
            return "닉네임을 입력하세요.";
        }
        if (!NicknamePattern().IsMatch(nickname))
        {
            return $"닉네임은 한글·영문·숫자 {NicknameMinLength}~{NicknameMaxLength}자로 입력하세요.";
        }
        if (Reserved.Any(r => nickname.Contains(r, StringComparison.OrdinalIgnoreCase)))
        {
            return "쓸 수 없는 닉네임입니다.";
        }
        // SQL Server 기본 정렬은 대소문자를 구분하지 않으므로 같은 닉네임으로 본다
        if (await db.Users.AnyAsync(u => u.Nickname == nickname && u.Id != exceptUserId, ct))
        {
            return "이미 쓰고 있는 닉네임입니다.";
        }
        return null;
    }
}
