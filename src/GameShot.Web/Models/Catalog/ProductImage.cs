namespace GameShot.Web.Models.Catalog;

/// <summary>상품 상세에 보여줄 추가 이미지</summary>
public class ProductImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public required string Path { get; set; }

    public int SortOrder { get; set; }
}
