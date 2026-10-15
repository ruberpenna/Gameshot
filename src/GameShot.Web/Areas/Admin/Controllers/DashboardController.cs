using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin")]
public class DashboardController(AdminDashboardService dashboard) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await dashboard.GetAsync(ct));
}
