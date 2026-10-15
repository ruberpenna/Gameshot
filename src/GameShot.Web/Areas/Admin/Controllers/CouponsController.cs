using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin/coupons")]
public class CouponsController(BenefitAdminService admin, BenefitService benefits) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await admin.ListCouponsAsync(ct));

    [HttpGet("new")]
    public IActionResult Create()
    {
        ViewData["Id"] = null;
        return View("Edit", new CouponForm());
    }

    [HttpPost("new")]
    public Task<IActionResult> Create(CouponForm form, CancellationToken ct) => Save(null, form, ct);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var coupon = await admin.GetCouponAsync(id, ct);
        if (coupon is null)
        {
            return NotFound();
        }
        ViewData["Id"] = id;
        return View(CouponForm.From(coupon));
    }

    [HttpPost("{id:int}")]
    public Task<IActionResult> Edit(int id, CouponForm form, CancellationToken ct) => Save(id, form, ct);

    [HttpPost("{id:int}/issue")]
    public async Task<IActionResult> Issue(int id, string? email, CancellationToken ct)
    {
        var result = await benefits.IssueToEmailAsync(id, email, ct);
        Flash(result.Success, result.Success ? $"{email?.Trim()}: {result.Message}" : result.Message);
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<IActionResult> Save(int? id, CouponForm form, CancellationToken ct)
    {
        ViewData["Id"] = id;
        if (!ModelState.IsValid)
        {
            return View("Edit", form);
        }
        var result = await admin.SaveCouponAsync(id, form, ct);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Message);
            return View("Edit", form);
        }
        Flash(true, result.Message);
        return RedirectToAction(nameof(Edit), new { id = result.Id });
    }
}
