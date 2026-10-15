using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models;
using GameShot.Web.Services.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameShot.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel(UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public string UserId { get; set; } = "";
        public string Code { get; set; } = "";

        [Display(Name = "새 비밀번호")]
        [Required(ErrorMessage = "{0}를 입력하세요.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "{0}는 {2}자 이상이어야 합니다.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "새 비밀번호 확인")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "새 비밀번호가 서로 다릅니다.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public IActionResult OnGet(string? userId = null, string? code = null)
    {
        if (string.IsNullOrEmpty(userId) || AccountMailer.Decode(code) is null)
        {
            return BadRequest("재설정 링크가 올바르지 않습니다.");
        }
        Input = new InputModel { UserId = userId, Code = code! };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByIdAsync(Input.UserId);
        var token = AccountMailer.Decode(Input.Code);
        if (user is null || token is null)
        {
            ModelState.AddModelError(string.Empty, "재설정 링크가 올바르지 않거나 만료되었습니다. 다시 요청해 주세요.");
            return Page();
        }

        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code.StartsWith("Password") ? "Input.Password" : string.Empty,
                    error.Code == "InvalidToken" ? "재설정 링크가 올바르지 않거나 만료되었습니다. 다시 요청해 주세요." : error.Description);
            }
            return Page();
        }

        // 비밀번호를 여러 번 틀려 잠겼던 계정도 재설정하면 바로 들어갈 수 있게
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return RedirectToPage("./ResetPasswordConfirmation");
    }
}
