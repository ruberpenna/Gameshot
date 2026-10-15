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
        var preorders = (await catalog.SearchAsync(new ProductFilter { PreorderOnly = true }, ct)).Items
            .OrderBy(c => c.ReleaseDate).Take(4).ToList();
        var discounted = (await catalog.SearchAsync(new ProductFilter { DiscountOnly = true, InStockOnly = true }, ct)).Items
            .OrderByDescending(c => c.MaxDiscountRate).Take(4).ToList();
        var newest = (await catalog.SearchAsync(new ProductFilter { Sort = ProductSort.Newest }, ct)).Items
            .Where(c => c.ReleaseDate is null || c.ReleaseDate <= catalog.Today).Take(8).ToList();

        var shown = preorders.Concat(discounted).Concat(newest).Select(c => c.Id);
        return View(new HomeViewModel
        {
            Featured = preorders.FirstOrDefault() ?? newest.FirstOrDefault(),
            Preorders = preorders,
            Discounted = discounted,
            NewArrivals = newest,
            Today = catalog.Today,
            WishedIds = await wishlist.WishedAmongAsync(users.GetUserId(User), shown, ct),
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
