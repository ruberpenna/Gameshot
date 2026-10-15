using GameShot.Web.Models;
using GameShot.Web.Services.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameShot.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ConfirmEmailModel(UserManager<ApplicationUser> users) : PageModel
{
    public bool Succeeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId = null, string? code = null)
    {
        var token = AccountMailer.Decode(code);
        var user = string.IsNullOrEmpty(userId) ? null : await users.FindByIdAsync(userId);
        if (user is null || token is null)
        {
            return Page();
        }

        Succeeded = user.EmailConfirmed || (await users.ConfirmEmailAsync(user, token)).Succeeded;
        return Page();
    }
}
