using System.Security.Claims;
using GameShot.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GameShot.Web.Services.Identity;

/// <summary>로그인 쿠키에 닉네임을 넣어서, 화면마다 DB를 다시 읽지 않고 표시한다</summary>
public class GameShotClaimsFactory(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    public const string NicknameClaim = "gameshot:nickname";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.Nickname))
        {
            identity.AddClaim(new Claim(NicknameClaim, user.Nickname));
        }
        return identity;
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>화면에 보여줄 이름: 닉네임, 없으면 이메일 앞부분</summary>
    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(GameShotClaimsFactory.NicknameClaim)
        ?? user.Identity?.Name?.Split('@')[0]
        ?? "회원";
}
