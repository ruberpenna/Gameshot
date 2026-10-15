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
    CartContext cartContext,
    OrderService orders,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // 로그인 직후라면 비회원 장바구니를 여기서 합친다
        await cartContext.GetOwnerAsync(ct);
        var quote = await orders.QuoteAsync(UserId, null, 0, ct);
        if (!quote.Cart.CanCheckout)
        {
            TempData["FlashError"] = quote.Cart.Lines.Count == 0
                ? "장바구니가 비어 있습니다."
                : "주문할 수 없는 상품이 있습니다. 장바구니를 확인해 주세요.";
            return RedirectToAction("Index", "Cart");
        }

        var form = CheckoutForm.From(await orders.GetLastShippingAsync(UserId, ct));
        form.ExpectedTotal = quote.Price.Total;
        return View(new CheckoutViewModel { Form = form, Quote = quote });
    }

    /// <summary>쿠폰·포인트를 바꿀 때 주문서가 부르는 금액 계산 (JSON)</summary>
    [HttpGet("quote")]
    public async Task<IActionResult> Quote(int? userCouponId, int points, CancellationToken ct)
    {
        var q = await orders.QuoteAsync(UserId, userCouponId, points, ct);
        return Json(new
        {
            subtotal = q.Price.Subtotal,
            shippingFee = q.Price.ShippingFee,
            couponDiscount = q.Price.CouponDiscount,
            pointsUsed = q.Price.PointsUsed,
            total = q.Price.Total,
            maxPoints = q.MaxUsablePoints,
            userCouponId = q.AppliedUserCouponId,
            earnPoints = q.ExpectedEarnPoints,
            error = q.Error,
        });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] CheckoutForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return await Redisplay(form, ct);
        }

        var result = await orders.PlaceOrderAsync(UserId, form.ToShippingInfo(), form.ExpectedTotal,
            form.UserCouponId, form.PointsToUse, ct);
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
        var quote = await orders.QuoteAsync(UserId, form.UserCouponId, form.PointsToUse, ct);
        if (!quote.Cart.CanCheckout)
        {
            TempData["FlashError"] = ModelState[""]?.Errors.FirstOrDefault()?.ErrorMessage
                ?? "주문할 수 없는 상품이 있습니다. 장바구니를 확인해 주세요.";
            return RedirectToAction("Index", "Cart");
        }

        // 화면에는 지금 금액을 보여줘야 하므로 제출된 값 대신 새로 계산한 값을 쓴다
        foreach (var key in new[] { "Form.ExpectedTotal", "Form.UserCouponId", "Form.PointsToUse" })
        {
            ModelState.Remove(key);
        }
        form.ExpectedTotal = quote.Price.Total;
        form.UserCouponId = quote.AppliedUserCouponId;
        form.PointsToUse = quote.Price.PointsUsed;
        return View(new CheckoutViewModel { Form = form, Quote = quote });
    }
}
