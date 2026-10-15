using GameShot.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace GameShot.Web.Data;

/// <summary>
/// 관리자 역할을 만들고, 설정 "Admin:Emails"에 적힌 회원에게 관리자 역할을 준다.
/// 개발: dotnet user-secrets set "Admin:Emails:0" "me@example.com"
/// 운영: 환경 변수 Admin__Emails__0=me@example.com
/// 역할은 로그인할 때 쿠키에 담기므로, 새로 관리자가 된 회원은 다시 로그인해야 한다.
/// </summary>
public static class AdminBootstrap
{
    public const string AdminRole = "Admin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(AdminRole))
        {
            await roles.CreateAsync(new IdentityRole(AdminRole));
        }

        foreach (var email in config.GetSection("Admin:Emails").Get<string[]>() ?? [])
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("관리자로 지정된 {Email} 회원이 없습니다. 먼저 회원가입하세요.", email);
                continue;
            }
            if (!await users.IsInRoleAsync(user, AdminRole))
            {
                await users.AddToRoleAsync(user, AdminRole);
                logger.LogInformation("{Email} 회원에게 관리자 역할을 줬습니다.", email);
            }
        }
    }
}
