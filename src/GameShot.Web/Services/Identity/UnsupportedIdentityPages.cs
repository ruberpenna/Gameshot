namespace GameShot.Web.Services.Identity;

/// <summary>
/// Identity UI 기본 페이지 중 직접 만들지 않은 것은 막는다 (404).
/// 메일 발송·2단계 인증·외부 로그인이 없어 동작하지 않거나, 개인정보 삭제처럼 주문 기록 때문에 오류가 나는 페이지들.
/// 기능을 붙일 때 해당 페이지를 만들고 여기서 빼면 된다.
/// </summary>
public static class UnsupportedIdentityPages
{
    public static readonly string[] Paths =
    [
        "/Identity/Account/ConfirmEmail",
        "/Identity/Account/ConfirmEmailChange",
        "/Identity/Account/ExternalLogin",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ForgotPasswordConfirmation",
        "/Identity/Account/LoginWith2fa",
        "/Identity/Account/LoginWithRecoveryCode",
        "/Identity/Account/RegisterConfirmation",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResetPasswordConfirmation",
        "/Identity/Account/Manage/DeletePersonalData",
        "/Identity/Account/Manage/Disable2fa",
        "/Identity/Account/Manage/DownloadPersonalData",
        "/Identity/Account/Manage/Email",
        "/Identity/Account/Manage/EnableAuthenticator",
        "/Identity/Account/Manage/ExternalLogins",
        "/Identity/Account/Manage/GenerateRecoveryCodes",
        "/Identity/Account/Manage/PersonalData",
        "/Identity/Account/Manage/ResetAuthenticator",
        "/Identity/Account/Manage/SetPassword",
        "/Identity/Account/Manage/ShowRecoveryCodes",
        "/Identity/Account/Manage/TwoFactorAuthentication",
    ];

    public static IApplicationBuilder BlockUnsupportedIdentityPages(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (Paths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
        });
}
