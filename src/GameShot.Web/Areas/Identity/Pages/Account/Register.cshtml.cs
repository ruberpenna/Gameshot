using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models;
using GameShot.Web.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    MemberService members,
    ILogger<RegisterModel> logger) : AccountPageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Display(Name = "이메일")]
        [Required(ErrorMessage = "{0}을 입력하세요.")]
        [EmailAddress(ErrorMessage = "이메일 형식이 아닙니다.")]
        [StringLength(100)]
        public string Email { get; set; } = "";

        [Display(Name = "닉네임")]
        [Required(ErrorMessage = "{0}을 입력하세요.")]
        public string Nickname { get; set; } = "";

        [Display(Name = "비밀번호")]
        [Required(ErrorMessage = "{0}를 입력하세요.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "{0}는 {2}자 이상이어야 합니다.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "비밀번호 확인")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "비밀번호가 서로 다릅니다.")]
        public string ConfirmPassword { get; set; } = "";

        [Display(Name = "이용약관과 개인정보 수집·이용에 동의합니다 (필수)")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "약관에 동의해야 가입할 수 있습니다.")]
        public bool AgreeTerms { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        SetReturnUrl(returnUrl);
        return signIn.IsSignedIn(User) ? RedirectToReturnUrl() : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, CancellationToken ct = default)
    {
        SetReturnUrl(returnUrl);
        if (ModelState.IsValid && await members.ValidateNicknameAsync(Input.Nickname, null, ct) is { } nicknameError)
        {
            ModelState.AddModelError("Input.Nickname", nicknameError);
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email, Nickname = Input.Nickname.Trim() };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, Input.Password);
        }
        catch (DbUpdateException)
        {
            // 같은 닉네임으로 동시에 가입한 경우: 유니크 인덱스가 막는다
            ModelState.AddModelError("Input.Nickname", "이미 쓰고 있는 닉네임입니다.");
            return Page();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var key = error.Code.StartsWith("Password") ? "Input.Password"
                    : error.Code is "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" ? "Input.Email"
                    : string.Empty;
                ModelState.AddModelError(key, error.Description);
            }
            return Page();
        }

        logger.LogInformation("회원가입: {Email}", email);
        await signIn.SignInAsync(user, isPersistent: false);
        TempData["FlashSuccess"] = $"{user.Nickname}님, 가입을 환영합니다!";
        return RedirectToReturnUrl();
    }
}
