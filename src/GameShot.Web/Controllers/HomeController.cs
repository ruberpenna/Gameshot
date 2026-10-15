using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using GameShot.Web.Models;
using GameShot.Web.Services;

namespace GameShot.Web.Controllers;

public class HomeController(CatalogService catalog, WishlistService wishlist, UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var latest = await catalog.SearchAsync(new ProductFilter { Sort = ProductSort.Newest }, ct);
        var cards = latest.Items.Take(8).ToList();
        return View(new HomeViewModel
        {
            NewArrivals = cards,
            Today = catalog.Today,
            WishedIds = await wishlist.WishedAmongAsync(users.GetUserId(User), cards.Select(c => c.Id), ct),
        });
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
