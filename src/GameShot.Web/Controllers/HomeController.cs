using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using GameShot.Web.Models;
using GameShot.Web.Services;

namespace GameShot.Web.Controllers;

public class HomeController(CatalogService catalog) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var latest = await catalog.SearchAsync(new ProductFilter { Sort = ProductSort.Newest }, ct);
        return View(new HomeViewModel { NewArrivals = latest.Items.Take(8).ToList(), Today = catalog.Today });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
