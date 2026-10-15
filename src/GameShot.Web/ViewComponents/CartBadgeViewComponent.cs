using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.ViewComponents;

/// <summary>상단 메뉴의 장바구니 링크와 담긴 상품 수</summary>
public class CartBadgeViewComponent(CartService carts, CartContext cartContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var owner = await cartContext.GetOwnerAsync(HttpContext.RequestAborted);
        return View(await carts.CountAsync(owner, HttpContext.RequestAborted));
    }
}
