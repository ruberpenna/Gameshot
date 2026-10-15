using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class WishlistController(WishlistService wishlist, CatalogService catalog, UserManager<ApplicationUser> users) : Controller
{
    /// <summary>찜 토글. fetch로 부르면 JSON, 폼 제출이면 원래 페이지로 돌아간다.</summary>
    [HttpPost("wishlist/{productId:int}")]
    public async Task<IActionResult> Toggle(int productId, string? returnUrl, CancellationToken ct)
    {
        var wished = await wishlist.ToggleAsync(UserId, productId, ct);
        if (Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return wished is null ? NotFound() : Json(new { wished });
        }

        if (wished is null)
        {
            return NotFound();
        }
        TempData["FlashSuccess"] = wished.Value ? "찜했습니다." : "찜을 해제했습니다.";
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpGet("my/wishlist")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var ids = await wishlist.ListProductIdsAsync(UserId, ct);
        var result = ids.Count == 0
            ? new PagedResult<ProductCard>([], 1, CatalogService.PageSize, 0)
            : await catalog.SearchAsync(new ProductFilter { Ids = ids, Page = page }, ct);
        return View(new WishlistViewModel { Result = result, Today = catalog.Today });
    }

    private string UserId => users.GetUserId(User)!;
}
