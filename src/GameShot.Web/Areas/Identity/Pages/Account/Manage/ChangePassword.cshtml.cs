using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameShot.Web.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class ChangePasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Display(Name = "현재 비밀번호")]
        [Required(ErrorMessage = "{0}를 입력하세요.")]
        [DataType(DataType.Password)]
        public string OldPassword { get; set; } = "";

        [Display(Name = "새 비밀번호")]
        [Required(ErrorMessage = "{0}를 입력하세요.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "{0}는 {2}자 이상이어야 합니다.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Display(Name = "새 비밀번호 확인")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "새 비밀번호가 서로 다릅니다.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await users.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var key = error.Code == "PasswordMismatch" ? "Input.OldPassword"
                    : error.Code.StartsWith("Password") ? "Input.NewPassword"
                    : string.Empty;
                ModelState.AddModelError(key, error.Description);
            }
            return Page();
        }

        // 보안 스탬프가 바뀌었으므로 이 브라우저의 로그인을 새로 고친다 (다른 기기는 다음 검사 때 로그아웃)
        await signIn.RefreshSignInAsync(user);
        logger.LogInformation("비밀번호 변경: {UserId}", user.Id);
        TempData["FlashSuccess"] = "비밀번호를 바꿨습니다.";
        return RedirectToPage();
    }
}
