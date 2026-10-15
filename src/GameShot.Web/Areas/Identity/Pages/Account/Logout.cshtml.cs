using GameShot.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Identity.Pages.Account;

/// <summary>로그아웃은 POST로만 (다른 사이트의 링크·이미지로 로그아웃시키지 못하게)</summary>
[AllowAnonymous]
public class LogoutModel(SignInManager<ApplicationUser> signIn) : AccountPageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        SetReturnUrl(returnUrl);
        await signIn.SignOutAsync();
        TempData["FlashSuccess"] = "로그아웃했습니다.";
        return RedirectToReturnUrl();
    }
}
