using GameShot.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace GameShot.Web.Services;

/// <summary>
/// 현재 요청의 장바구니 주인을 정한다.
/// 비회원 장바구니 Id는 쿠키에 두고, 로그인하면 다음 요청에서 회원 장바구니로 합친 뒤 쿠키를 지운다.
/// </summary>
public class CartContext(IHttpContextAccessor accessor, UserManager<ApplicationUser> users, CartService carts)
{
    public const string GuestCookieName = "gs_cart";

    private CartOwner? _owner;

    private HttpContext Http => accessor.HttpContext
        ?? throw new InvalidOperationException("HTTP 요청 밖에서는 장바구니를 쓸 수 없습니다.");

    public async Task<CartOwner> GetOwnerAsync(CancellationToken ct = default)
    {
        if (_owner is not null)
        {
            return _owner;
        }

        var userId = users.GetUserId(Http.User);
        var guestId = ReadGuestCookie();

        if (userId is not null && guestId is { } id)
        {
            await carts.MergeGuestCartAsync(id, userId, ct);
            Http.Response.Cookies.Delete(GuestCookieName);
            guestId = null;
        }

        return _owner = new CartOwner(userId, userId is null ? guestId : null);
    }

    /// <summary>비회원이 장바구니를 새로 만들었을 때 쿠키에 기억</summary>
    public void RememberGuestCart(Guid cartId)
    {
        if (users.GetUserId(Http.User) is not null || ReadGuestCookie() == cartId)
        {
            return;
        }

        Http.Response.Cookies.Append(GuestCookieName, cartId.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = Http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(30),
        });
        _owner = new CartOwner(null, cartId);
    }

    private Guid? ReadGuestCookie() =>
        Http.Request.Cookies.TryGetValue(GuestCookieName, out var raw) && Guid.TryParseExact(raw, "N", out var id)
            ? id
            : null;
}
