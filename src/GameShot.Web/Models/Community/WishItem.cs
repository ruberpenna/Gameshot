using GameShot.Web.Models.Catalog;

namespace GameShot.Web.Models.Community;

/// <summary>찜한 상품 (상품 단위, SKU가 아니라)</summary>
public class WishItem
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 상품 리뷰. 그 상품이 든 주문이 배송 완료된 회원만, 상품당 하나 쓸 수 있다.
/// </summary>
public class Review
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>별점 1~5</summary>
    public int Rating { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>관리자가 숨긴 리뷰는 목록·평균에서 빠진다</summary>
    public bool IsHidden { get; set; }
}
