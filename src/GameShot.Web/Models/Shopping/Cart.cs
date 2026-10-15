using GameShot.Web.Models.Catalog;

namespace GameShot.Web.Models.Shopping;

/// <summary>
/// 장바구니. 회원은 UserId로, 비회원은 쿠키에 담긴 Id로 찾는다.
/// 가격은 저장하지 않고 항상 SKU의 현재 판매가를 쓴다.
/// </summary>
public class Cart
{
    public Guid Id { get; set; }

    /// <summary>회원 장바구니면 사용자 Id, 비회원이면 null</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CartItem> Items { get; set; } = [];
}

public class CartItem
{
    public int Id { get; set; }

    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
