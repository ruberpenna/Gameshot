using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

/// <summary>마이페이지: 쿠폰함, 포인트</summary>
[Authorize]
[Route("my")]
[AutoValidateAntiforgeryToken]
public class MyController(BenefitService benefits, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("coupons")]
    public async Task<IActionResult> Coupons(CancellationToken ct) =>
        View(await benefits.GetWalletAsync(UserId, ct));

    [HttpPost("coupons")]
    public async Task<IActionResult> RegisterCoupon(string? code, CancellationToken ct)
    {
        var result = await benefits.RegisterCodeAsync(UserId, code, ct);
        TempData[result.Success ? "FlashSuccess" : "FlashError"] = result.Message;
        return RedirectToAction(nameof(Coupons));
    }

    [HttpGet("points")]
    public async Task<IActionResult> Points(CancellationToken ct)
    {
        ViewData["Balance"] = await benefits.GetBalanceAsync(UserId, ct);
        return View(await benefits.GetHistoryAsync(UserId, ct: ct));
    }

    private string UserId => users.GetUserId(User)!;
}
