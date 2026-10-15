namespace GameShot.Web.Models.Catalog;

/// <summary>게임기 플랫폼 (PS5, Switch 2 ...)</summary>
public class Platform
{
    public int Id { get; set; }

    /// <summary>URL·필터에 쓰는 짧은 코드 (예: ps5)</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = [];
}
