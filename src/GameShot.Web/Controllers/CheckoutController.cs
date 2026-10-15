using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

/// <summary>주문서 (회원만). 비회원이 들어오면 로그인 화면으로 갔다가, 로그인 후 장바구니가 합쳐진 채로 돌아온다.</summary>
[Authorize]
[Route("checkout")]
[AutoValidateAntiforgeryToken]
public class CheckoutController(
    CartService carts,
    CartContext cartContext,
    OrderService orders,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cart = await carts.GetViewAsync(await cartContext.GetOwnerAsync(ct), ct);
        if (!cart.CanCheckout)
        {
            TempData["FlashError"] = cart.Lines.Count == 0
                ? "장바구니가 비어 있습니다."
                : "주문할 수 없는 상품이 있습니다. 장바구니를 확인해 주세요.";
            return RedirectToAction("Index", "Cart");
        }

        var form = CheckoutForm.From(await orders.GetLastShippingAsync(UserId, ct));
        form.ExpectedTotal = cart.Total;
        return View(new CheckoutViewModel { Form = form, Cart = cart });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] CheckoutForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return await Redisplay(form, ct);
        }

        var result = await orders.PlaceOrderAsync(UserId, form.ToShippingInfo(), form.ExpectedTotal, ct);
        if (!result.Success)
        {
            // 입력한 배송지는 유지하고, 바뀐 금액으로 다시 보여준다
            ModelState.AddModelError("", result.Message);
            return await Redisplay(form, ct);
        }

        return RedirectToAction("Pay", "Orders", new { orderNumber = result.OrderNumber });
    }

    private string UserId => users.GetUserId(User)!;

    private async Task<IActionResult> Redisplay(CheckoutForm form, CancellationToken ct)
    {
        var cart = await carts.GetViewAsync(await cartContext.GetOwnerAsync(ct), ct);
        if (!cart.CanCheckout)
        {
            TempData["FlashError"] = ModelState[""]?.Errors.FirstOrDefault()?.ErrorMessage
                ?? "주문할 수 없는 상품이 있습니다. 장바구니를 확인해 주세요.";
            return RedirectToAction("Index", "Cart");
        }

        // 화면에는 지금 금액을 보여줘야 하므로 제출된 값 대신 새로 계산한 값을 쓴다
        ModelState.Remove("Form.ExpectedTotal");
        form.ExpectedTotal = cart.Total;
        return View(new CheckoutViewModel { Form = form, Cart = cart });
    }
}
