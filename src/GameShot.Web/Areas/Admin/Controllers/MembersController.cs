using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin/members")]
public class MembersController(BenefitAdminService admin, BenefitService benefits) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken ct = default)
    {
        ViewData["Q"] = q;
        return View(await admin.SearchMembersAsync(q, page, ct));
    }

    [HttpGet("{id}/points")]
    public async Task<IActionResult> Points(string id, CancellationToken ct)
    {
        var member = await admin.GetMemberAsync(id, ct);
        if (member is null)
        {
            return NotFound();
        }
        ViewData["Member"] = member;
        return View(await benefits.GetHistoryAsync(id, ct: ct));
    }

    [HttpPost("{id}/points")]
    public async Task<IActionResult> AdjustPoints(string id, int amount, string? reason, CancellationToken ct)
    {
        if (await admin.GetMemberAsync(id, ct) is null)
        {
            return NotFound();
        }
        var result = await benefits.AdjustPointsAsync(id, amount, reason, ct);
        Flash(result.Success, result.Message);
        return RedirectToAction(nameof(Points), new { id });
    }
}
