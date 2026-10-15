namespace GameShot.Web.Models.Catalog;

public enum ItemCondition
{
    New = 0,
    Used = 1,
}

/// <summary>
/// 판매 단위(SKU). 같은 타이틀이라도 플랫폼·에디션·언어·신품/중고마다 가격과 재고가 다르다.
/// </summary>
public class ProductVariant
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    /// <summary>재고 관리용 고유 코드</summary>
    public required string Sku { get; set; }

    /// <summary>에디션 (예: 일반판, 디럭스 에디션)</summary>
    public string Edition { get; set; } = "일반판";

    /// <summary>언어 (예: 한글판, 영문판)</summary>
    public string Language { get; set; } = "한글판";

    public ItemCondition Condition { get; set; } = ItemCondition.New;

    /// <summary>정가 (원)</summary>
    public int ListPrice { get; set; }

    /// <summary>판매가 (원)</summary>
    public int SalePrice { get; set; }

    public int Stock { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSoldOut => Stock <= 0;

    public bool IsDiscounted => SalePrice < ListPrice;

    /// <summary>할인율 (%), 소수점 버림</summary>
    public int DiscountRate => ListPrice > 0 && IsDiscounted ? (ListPrice - SalePrice) * 100 / ListPrice : 0;
}
