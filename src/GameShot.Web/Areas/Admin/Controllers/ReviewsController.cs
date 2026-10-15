using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin/reviews")]
public class ReviewsController(ReviewService reviews) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool? hidden, int page = 1, CancellationToken ct = default)
    {
        ViewData["Hidden"] = hidden;
        return View(await reviews.ListForAdminAsync(hidden, page, ct));
    }

    [HttpPost("{id:int}/hidden")]
    public async Task<IActionResult> SetHidden(int id, bool hidden, bool? filter, int page = 1, CancellationToken ct = default)
    {
        var result = await reviews.SetHiddenAsync(id, hidden, ct);
        Flash(result.Success, result.Message);
        return RedirectToAction(nameof(Index), new { hidden = filter, page });
    }
}
