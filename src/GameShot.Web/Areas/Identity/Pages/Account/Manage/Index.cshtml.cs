using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models;
using GameShot.Web.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Areas.Identity.Pages.Account.Manage;

/// <summary>프로필: 닉네임·연락처 수정 (이메일은 메일 인증을 붙이기 전까지 바꿀 수 없음)</summary>
[Authorize]
public class IndexModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    MemberService members) : PageModel
{
    public string Email { get; private set; } = "";
    public DateTime JoinedAt { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Display(Name = "닉네임")]
        [Required(ErrorMessage = "{0}을 입력하세요.")]
        public string Nickname { get; set; } = "";

        [Display(Name = "연락처")]
        [RegularExpression(@"^0\d{1,2}-?\d{3,4}-?\d{4}$", ErrorMessage = "연락처 형식이 올바르지 않습니다. (예: 010-1234-5678)")]
        public string? PhoneNumber { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }
        Load(user);
        Input = new InputModel { Nickname = user.Nickname ?? "", PhoneNumber = user.PhoneNumber };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }
        Load(user);

        if (ModelState.IsValid && await members.ValidateNicknameAsync(Input.Nickname, user.Id, ct) is { } nicknameError)
        {
            ModelState.AddModelError("Input.Nickname", nicknameError);
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.Nickname = Input.Nickname.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : GameShot.Web.Models.CheckoutForm.NormalizePhone(Input.PhoneNumber);
        IdentityResult result;
        try
        {
            result = await users.UpdateAsync(user);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("Input.Nickname", "이미 쓰고 있는 닉네임입니다.");
            return Page();
        }
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // 쿠키의 닉네임을 새로 고친다
        await signIn.RefreshSignInAsync(user);
        TempData["FlashSuccess"] = "프로필을 저장했습니다.";
        return RedirectToPage();
    }

    private void Load(ApplicationUser user)
    {
        Email = user.Email ?? "";
        JoinedAt = user.CreatedAt;
    }
}
