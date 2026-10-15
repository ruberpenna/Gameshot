using System.ComponentModel.DataAnnotations;
using GameShot.Web.Services.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameShot.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel(AccountMailer mailer) : PageModel
{
    [BindProperty]
    [Display(Name = "가입한 이메일")]
    [Required(ErrorMessage = "{0}을 입력하세요.")]
    [EmailAddress(ErrorMessage = "이메일 형식이 아닙니다.")]
    public string Email { get; set; } = "";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        await mailer.RequestPasswordResetAsync(Email, ct);
        // 가입 여부와 상관없이 같은 화면으로 (어떤 이메일이 가입했는지 알아낼 수 없게)
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
