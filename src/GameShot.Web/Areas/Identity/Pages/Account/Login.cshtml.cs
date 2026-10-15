using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<ApplicationUser> signIn, ILogger<LoginModel> logger) : AccountPageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Display(Name = "이메일")]
        [Required(ErrorMessage = "{0}을 입력하세요.")]
        [EmailAddress(ErrorMessage = "이메일 형식이 아닙니다.")]
        public string Email { get; set; } = "";

        [Display(Name = "비밀번호")]
        [Required(ErrorMessage = "{0}를 입력하세요.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "로그인 상태 유지")]
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        SetReturnUrl(returnUrl);
        // 이미 로그인했으면 로그인 화면을 보여줄 필요가 없다
        return signIn.IsSignedIn(User) ? RedirectToReturnUrl() : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        SetReturnUrl(returnUrl);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 아이디는 이메일 (가입할 때 UserName = Email)
        var result = await signIn.PasswordSignInAsync(Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return RedirectToReturnUrl();
        }
        if (result.IsLockedOut)
        {
            logger.LogWarning("로그인 실패가 반복되어 계정이 잠겼습니다: {Email}", Input.Email);
            return RedirectToPage("./Lockout");
        }

        // 이메일이 없는지 비밀번호가 틀렸는지 구분해서 알려주지 않는다 (가입 여부 노출 방지)
        ModelState.AddModelError(string.Empty, "이메일 또는 비밀번호가 맞지 않습니다.");
        return Page();
    }
}
