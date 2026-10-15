using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

[Route("products")]
[AutoValidateAntiforgeryToken]
public class ProductsController(
    CatalogService catalog,
    WishlistService wishlist,
    ReviewService reviews,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductFilter filter, CancellationToken ct)
    {
        var result = await catalog.SearchAsync(filter with { Ids = null }, ct);
        var model = new ProductListViewModel
        {
            Filter = filter,
            Result = result,
            Platforms = await catalog.GetPlatformsAsync(ct),
            Genres = await catalog.GetGenresAsync(ct),
            Today = catalog.Today,
            WishedIds = await wishlist.WishedAmongAsync(UserId, result.Items.Select(c => c.Id), ct),
        };
        return View(model);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, int reviewPage = 1, CancellationToken ct = default)
    {
        var product = await catalog.FindBySlugAsync(slug, ct);
        if (product is null)
        {
            return NotFound();
        }
        var wished = (await wishlist.WishedAmongAsync(UserId, [product.Id], ct)).Contains(product.Id);
        var productReviews = await reviews.GetForProductAsync(product.Id, UserId, reviewPage, ct);
        return View(new ProductDetailViewModel(product, catalog.Today, wished, productReviews));
    }

    [Authorize]
    [HttpPost("{slug}/reviews")]
    public async Task<IActionResult> WriteReview(string slug, int rating, string? content, CancellationToken ct)
    {
        var product = await catalog.FindBySlugAsync(slug, ct);
        if (product is null)
        {
            return NotFound();
        }
        return Done(await reviews.WriteAsync(UserId!, product.Id, rating, content, ct), slug, content);
    }

    [Authorize]
    [HttpPost("{slug}/reviews/{reviewId:int}")]
    public async Task<IActionResult> EditReview(string slug, int reviewId, int rating, string? content, CancellationToken ct) =>
        Done(await reviews.EditAsync(UserId!, reviewId, rating, content, ct), slug, content);

    [Authorize]
    [HttpPost("{slug}/reviews/{reviewId:int}/delete")]
    public async Task<IActionResult> DeleteReview(string slug, int reviewId, CancellationToken ct) =>
        Done(await reviews.DeleteAsync(UserId!, reviewId, ct), slug, null);

    private string? UserId => users.GetUserId(User);

    private RedirectResult Done(BenefitResult result, string slug, string? content)
    {
        TempData[result.Success ? "FlashSuccess" : "FlashError"] = result.Message;
        if (!result.Success && content is not null)
        {
            // 실패했을 때 쓰던 글이 사라지지 않게
            TempData["ReviewDraft"] = content;
        }
        return Redirect(Url.Action(nameof(Details), new { slug }) + "#reviews");
    }
}
