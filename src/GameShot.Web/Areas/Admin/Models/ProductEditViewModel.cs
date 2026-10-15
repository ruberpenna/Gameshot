using GameShot.Web.Models.Catalog;
using GameShot.Web.Services;

namespace GameShot.Web.Areas.Admin.Models;

public class ProductEditViewModel
{
    /// <summary>새 상품이면 null</summary>
    public int? Id { get; init; }
    public required ProductForm Form { get; init; }
    public string? ThumbnailPath { get; init; }
    public required IReadOnlyList<ProductVariant> Variants { get; init; }
    public required IReadOnlyList<Platform> Platforms { get; init; }
    public required IReadOnlyList<Genre> Genres { get; init; }

    public bool IsNew => Id is null;
}
