using GameShot.Web.Models.Catalog;
using GameShot.Web.Services;

namespace GameShot.Web.Models;

public class ProductListViewModel
{
    public required ProductFilter Filter { get; init; }
    public required PagedResult<ProductCard> Result { get; init; }
    public required IReadOnlyList<Platform> Platforms { get; init; }
    public required IReadOnlyList<Genre> Genres { get; init; }
    public DateOnly Today { get; init; }
}

public record ProductDetailViewModel(Product Product, DateOnly Today)
{
    public bool IsPreorder => Product.IsPreorder(Today);
}

public class HomeViewModel
{
    public required IReadOnlyList<ProductCard> NewArrivals { get; init; }
    public DateOnly Today { get; init; }
}
