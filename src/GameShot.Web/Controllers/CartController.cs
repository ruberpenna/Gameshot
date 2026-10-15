using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

[Route("cart")]
[AutoValidateAntiforgeryToken]
public class CartController(CartService carts, CartContext cartContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var owner = await cartContext.GetOwnerAsync(ct);
        return View(await carts.GetViewAsync(owner, ct));
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add(int variantId, int quantity, string? returnUrl, CancellationToken ct)
    {
        var owner = await cartContext.GetOwnerAsync(ct);
        var result = await carts.AddAsync(owner, variantId, quantity, ct);
        if (result.CartId is { } cartId)
        {
            cartContext.RememberGuestCart(cartId);
        }

        Flash(result);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpPost("items/{itemId:int}")]
    public async Task<IActionResult> Update(int itemId, int quantity, CancellationToken ct)
    {
        var owner = await cartContext.GetOwnerAsync(ct);
        Flash(await carts.UpdateQuantityAsync(owner, itemId, quantity, ct));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("items/{itemId:int}/remove")]
    public async Task<IActionResult> Remove(int itemId, CancellationToken ct)
    {
        var owner = await cartContext.GetOwnerAsync(ct);
        Flash(await carts.RemoveAsync(owner, itemId, ct));
        return RedirectToAction(nameof(Index));
    }

    private void Flash(CartResult result)
    {
        TempData[result.Success ? "FlashSuccess" : "FlashError"] = result.Message;
    }
}
