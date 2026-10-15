using GameShot.Web.Areas.Admin.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin/products")]
public class ProductsController(ProductAdminService products, CatalogService catalog, IImageStorage images) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken ct = default)
    {
        ViewData["Q"] = q;
        return View(await products.ListAsync(q, page, ct));
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Edit", await BuildEditModel(null, new ProductForm(), ct));

    [HttpPost("new")]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] ProductForm form, IFormFile? thumbnail, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", await BuildEditModel(null, form, ct));
        }

        var upload = await SaveThumbnail(thumbnail, ct);
        if (upload is { Success: false })
        {
            ModelState.AddModelError("", upload.Error!);
            return View("Edit", await BuildEditModel(null, form, ct));
        }

        var result = await products.CreateAsync(form, upload?.Path, ct);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Message);
            return View("Edit", await BuildEditModel(null, form, ct));
        }

        Flash(true, result.Message);
        return RedirectToAction(nameof(Edit), new { id = result.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var product = await products.GetAsync(id, ct);
        return product is null ? NotFound() : View(await BuildEditModel(id, ProductForm.From(product), ct));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Form")] ProductForm form, IFormFile? thumbnail, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildEditModel(id, form, ct));
        }

        var upload = await SaveThumbnail(thumbnail, ct);
        if (upload is { Success: false })
        {
            ModelState.AddModelError("", upload.Error!);
            return View(await BuildEditModel(id, form, ct));
        }

        var result = await products.UpdateAsync(id, form, upload?.Path, ct);
        if (!result.Success)
        {
            ModelState.AddModelError("", result.Message);
            return View(await BuildEditModel(id, form, ct));
        }

        Flash(true, result.Message);
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:int}/variants")]
    public async Task<IActionResult> AddVariant(int id, VariantForm form, CancellationToken ct)
    {
        var result = ModelState.IsValid
            ? await products.AddVariantAsync(id, form, ct)
            : AdminResult.Fail(FirstModelError());
        Flash(result.Success, result.Message);
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("variants/{variantId:int}")]
    public async Task<IActionResult> UpdateVariant(int variantId, int productId, VariantForm form, CancellationToken ct)
    {
        var result = ModelState.IsValid
            ? await products.UpdateVariantAsync(variantId, form, ct)
            : AdminResult.Fail(FirstModelError());
        Flash(result.Success, result.Message);
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    private async Task<ImageSaveResult?> SaveThumbnail(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }
        await using var stream = file.OpenReadStream();
        return await images.SaveProductImageAsync(stream, file.FileName, ct);
    }

    private async Task<ProductEditViewModel> BuildEditModel(int? id, ProductForm form, CancellationToken ct)
    {
        var product = id is null ? null : await products.GetAsync(id.Value, ct);
        return new ProductEditViewModel
        {
            Id = id,
            Form = form,
            ThumbnailPath = product?.ThumbnailPath,
            Variants = product?.Variants.ToList() ?? [],
            Platforms = await catalog.GetPlatformsAsync(ct),
            Genres = await catalog.GetGenresAsync(ct),
        };
    }

    private string FirstModelError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => m.Length > 0)
        ?? "입력값을 확인하세요.";
}
