using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameShot.Web.Areas.Identity.Pages.Account;

/// <summary>계정 화면 공통: 로그인 후 돌아갈 주소는 이 사이트 안의 주소만 허용 (오픈 리다이렉트 방지)</summary>
public abstract class AccountPageModel : PageModel
{
    public string ReturnUrl { get; protected set; } = "/";

    protected void SetReturnUrl(string? returnUrl) =>
        ReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    protected LocalRedirectResult RedirectToReturnUrl() => LocalRedirect(ReturnUrl);
}
