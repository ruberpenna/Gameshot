using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

[Route("products")]
public class ProductsController(CatalogService catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductFilter filter, CancellationToken ct)
    {
        var model = new ProductListViewModel
        {
            Filter = filter,
            Result = await catalog.SearchAsync(filter, ct),
            Platforms = await catalog.GetPlatformsAsync(ct),
            Genres = await catalog.GetGenresAsync(ct),
            Today = catalog.Today,
        };
        return View(model);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var product = await catalog.FindBySlugAsync(slug, ct);
        if (product is null)
        {
            return NotFound();
        }
        return View(new ProductDetailViewModel(product, catalog.Today));
    }
}
