using GameShot.Web.Data;
using GameShot.Web.Models.Catalog;
using GameShot.Web.Models.Shopping;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

/// <summary>장바구니 주인. 회원이면 UserId, 비회원이면 쿠키의 장바구니 Id.</summary>
public record CartOwner(string? UserId, Guid? GuestCartId);

public enum CartLineStatus
{
    Ok,
    /// <summary>재고가 담은 수량보다 적음</summary>
    InsufficientStock,
    SoldOut,
    /// <summary>판매 중지되었거나 비공개된 상품</summary>
    Unavailable,
}

public record CartLine(
    int ItemId,
    int VariantId,
    string ProductSlug,
    string ProductTitle,
    string? ThumbnailPath,
    string PlatformName,
    string Edition,
    string Language,
    ItemCondition Condition,
    int ListPrice,
    int UnitPrice,
    int Quantity,
    int Stock,
    CartLineStatus Status)
{
    public bool IsOrderable => Status == CartLineStatus.Ok;
    public int LineTotal => UnitPrice * Quantity;
}

public record CartView(IReadOnlyList<CartLine> Lines, int Subtotal, int ShippingFee)
{
    public static readonly CartView Empty = new([], 0, 0);

    public int Total => Subtotal + ShippingFee;
    public int TotalQuantity => Lines.Sum(l => l.Quantity);
    public bool HasProblems => Lines.Any(l => !l.IsOrderable);
    public bool CanCheckout => Lines.Count > 0 && !HasProblems;
}

/// <param name="CartId">담기에 성공했을 때의 장바구니 Id (비회원이면 이 값을 쿠키에 저장)</param>
public record CartResult(bool Success, string Message, Guid? CartId = null)
{
    public static CartResult Ok(string message, Guid? cartId = null) => new(true, message, cartId);
    public static CartResult Fail(string message) => new(false, message);
}

public class CartService(ApplicationDbContext db, TimeProvider clock)
{
    /// <summary>한 상품(SKU)을 한 번에 담을 수 있는 최대 수량</summary>
    public const int MaxQuantityPerItem = 10;
    public const int FreeShippingThreshold = 30_000;
    public const int ShippingFee = 3_000;

    public static int CalculateShippingFee(int subtotal) =>
        subtotal is > 0 and < FreeShippingThreshold ? ShippingFee : 0;

    /// <summary>
    /// SKU를 담는다. 장바구니가 없으면 새로 만든다(비회원은 결과의 CartId를 쿠키에 저장해야 함).
    /// </summary>
    public async Task<CartResult> AddAsync(CartOwner owner, int variantId, int quantity, CancellationToken ct = default)
    {
        if (quantity is < 1 or > MaxQuantityPerItem)
        {
            return CartResult.Fail($"수량은 1~{MaxQuantityPerItem}개 사이로 선택하세요.");
        }

        // 재고는 주문·취소 때 ExecuteUpdate로 바뀌므로, 추적 중인 낡은 값 대신 항상 DB에서 새로 읽는다
        var variant = await db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null || !variant.IsActive || !variant.Product.IsPublished)
        {
            return CartResult.Fail("판매 중인 상품이 아닙니다.");
        }
        if (variant.IsSoldOut)
        {
            return CartResult.Fail("품절된 상품입니다.");
        }

        var cart = await FindCartAsync(owner, ct) ?? CreateCart(owner);
        var item = cart.Items.FirstOrDefault(i => i.ProductVariantId == variantId);
        var current = item?.Quantity ?? 0;
        var limit = Math.Min(MaxQuantityPerItem, variant.Stock);
        if (current >= limit)
        {
            return CartResult.Fail(current >= MaxQuantityPerItem
                ? $"한 상품은 최대 {MaxQuantityPerItem}개까지 담을 수 있습니다."
                : $"재고가 {variant.Stock}개뿐이라 더 담을 수 없습니다.");
        }

        var newQuantity = Math.Min(current + quantity, limit);
        if (item is null)
        {
            cart.Items.Add(new CartItem { ProductVariantId = variantId, Quantity = newQuantity, AddedAt = Now });
        }
        else
        {
            item.Quantity = newQuantity;
        }
        cart.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);

        return newQuantity < current + quantity
            ? CartResult.Ok($"재고·수량 제한으로 {newQuantity}개까지만 담았습니다.", cart.Id)
            : CartResult.Ok("장바구니에 담았습니다.", cart.Id);
    }

    public async Task<CartResult> UpdateQuantityAsync(CartOwner owner, int itemId, int quantity, CancellationToken ct = default)
    {
        if (quantity is < 1 or > MaxQuantityPerItem)
        {
            return CartResult.Fail($"수량은 1~{MaxQuantityPerItem}개 사이로 선택하세요.");
        }

        var cart = await FindCartAsync(owner, ct);
        var item = cart?.Items.FirstOrDefault(i => i.Id == itemId);
        if (cart is null || item is null)
        {
            return CartResult.Fail("장바구니에 없는 상품입니다.");
        }

        var stock = await db.ProductVariants.Where(v => v.Id == item.ProductVariantId).Select(v => v.Stock).FirstAsync(ct);
        if (quantity > stock)
        {
            return CartResult.Fail(stock > 0 ? $"재고가 {stock}개 남았습니다." : "품절된 상품입니다.");
        }

        item.Quantity = quantity;
        cart.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return CartResult.Ok("수량을 변경했습니다.");
    }

    public async Task<CartResult> RemoveAsync(CartOwner owner, int itemId, CancellationToken ct = default)
    {
        var cart = await FindCartAsync(owner, ct);
        var item = cart?.Items.FirstOrDefault(i => i.Id == itemId);
        if (cart is null || item is null)
        {
            return CartResult.Fail("장바구니에 없는 상품입니다.");
        }

        db.CartItems.Remove(item);
        cart.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return CartResult.Ok("상품을 뺐습니다.");
    }

    /// <summary>
    /// 로그인 전에 담은 비회원 장바구니를 회원 장바구니로 합친다.
    /// 같은 SKU는 수량을 더하되 최대 수량을 넘지 않게 하고, 비회원 장바구니는 지운다.
    /// </summary>
    public async Task MergeGuestCartAsync(Guid guestCartId, string userId, CancellationToken ct = default)
    {
        var guest = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == guestCartId && c.UserId == null, ct);
        if (guest is null)
        {
            return;
        }

        if (guest.Items.Count > 0)
        {
            var owner = new CartOwner(userId, null);
            var cart = await FindCartAsync(owner, ct) ?? CreateCart(owner);
            foreach (var guestItem in guest.Items)
            {
                var item = cart.Items.FirstOrDefault(i => i.ProductVariantId == guestItem.ProductVariantId);
                if (item is null)
                {
                    cart.Items.Add(new CartItem
                    {
                        ProductVariantId = guestItem.ProductVariantId,
                        Quantity = guestItem.Quantity,
                        AddedAt = guestItem.AddedAt,
                    });
                }
                else
                {
                    item.Quantity = Math.Min(item.Quantity + guestItem.Quantity, MaxQuantityPerItem);
                }
            }
            cart.UpdatedAt = Now;
        }

        db.Carts.Remove(guest);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CartView> GetViewAsync(CartOwner owner, CancellationToken ct = default)
    {
        var cartId = await FindCartIdAsync(owner, ct);
        if (cartId is null)
        {
            return CartView.Empty;
        }

        var rows = await db.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId)
            .OrderBy(i => i.AddedAt)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                i.Quantity,
                Variant = i.ProductVariant,
                PlatformName = i.ProductVariant.Platform.Name,
                i.ProductVariant.Product.Slug,
                i.ProductVariant.Product.Title,
                i.ProductVariant.Product.ThumbnailPath,
                i.ProductVariant.Product.IsPublished,
            })
            .ToListAsync(ct);

        var lines = rows.Select(r =>
        {
            var v = r.Variant;
            var status = !v.IsActive || !r.IsPublished ? CartLineStatus.Unavailable
                : v.Stock <= 0 ? CartLineStatus.SoldOut
                : v.Stock < r.Quantity ? CartLineStatus.InsufficientStock
                : CartLineStatus.Ok;
            return new CartLine(r.Id, v.Id, r.Slug, r.Title, r.ThumbnailPath, r.PlatformName,
                v.Edition, v.Language, v.Condition, v.ListPrice, v.SalePrice, r.Quantity, v.Stock, status);
        }).ToList();

        // 주문할 수 없는 상품은 합계에서 뺀다
        var subtotal = lines.Where(l => l.IsOrderable).Sum(l => l.LineTotal);
        return new CartView(lines, subtotal, CalculateShippingFee(subtotal));
    }

    /// <summary>헤더 배지용: 담긴 상품 종류 수</summary>
    public async Task<int> CountAsync(CartOwner owner, CancellationToken ct = default)
    {
        var cartId = await FindCartIdAsync(owner, ct);
        return cartId is null ? 0 : await db.CartItems.CountAsync(i => i.CartId == cartId, ct);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private IQueryable<Cart> CartsOf(CartOwner owner) => owner switch
    {
        { UserId: { } userId } => db.Carts.Where(c => c.UserId == userId),
        // 비회원 쿠키로는 회원 장바구니를 열 수 없게 UserId == null 조건을 꼭 건다
        { GuestCartId: { } guestId } => db.Carts.Where(c => c.Id == guestId && c.UserId == null),
        _ => db.Carts.Where(_ => false),
    };

    private Task<Cart?> FindCartAsync(CartOwner owner, CancellationToken ct) =>
        CartsOf(owner).Include(c => c.Items).FirstOrDefaultAsync(ct);

    private async Task<Guid?> FindCartIdAsync(CartOwner owner, CancellationToken ct) =>
        await CartsOf(owner).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    private Cart CreateCart(CartOwner owner)
    {
        // Id는 항상 새로 만든다. 쿠키 값을 그대로 쓰면 조작된 쿠키로 다른 장바구니 Id와 충돌시킬 수 있다
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            UserId = owner.UserId,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Carts.Add(cart);
        return cart;
    }
}
