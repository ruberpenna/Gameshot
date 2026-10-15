namespace GameShot.Web.Models.Catalog;

/// <summary>
/// 게임 타이틀. 실제로 장바구니에 담기는 단위는 플랫폼·에디션·상태별 <see cref="ProductVariant"/>.
/// </summary>
public class Product
{
    public int Id { get; set; }

    public required string Title { get; set; }

    /// <summary>URL용 식별자 (예: far-cry-5-deluxe)</summary>
    public required string Slug { get; set; }

    public string? Publisher { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    /// <summary>wwwroot 기준 대표 이미지 경로</summary>
    public string? ThumbnailPath { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? GenreId { get; set; }
    public Genre? Genre { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = [];

    public ICollection<ProductImage> Images { get; set; } = [];

    /// <summary>발매일이 기준일 이후면 예약판매 상품</summary>
    public bool IsPreorder(DateOnly today) => ReleaseDate is { } date && date > today;
}
