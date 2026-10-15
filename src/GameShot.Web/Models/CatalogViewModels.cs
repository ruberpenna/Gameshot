using GameShot.Web.Models.Catalog;
using GameShot.Web.Services;

namespace GameShot.Web.Models;

/// <summary>상품 카드 partial 모델</summary>
public record ProductCardModel(ProductCard Card, DateOnly Today, bool IsWished);

public class ProductListViewModel
{
    public required ProductFilter Filter { get; init; }
    public required PagedResult<ProductCard> Result { get; init; }
    public required IReadOnlyList<Platform> Platforms { get; init; }
    public required IReadOnlyList<Genre> Genres { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlySet<int> WishedIds { get; init; } = new HashSet<int>();
}

public record ProductDetailViewModel(Product Product, DateOnly Today, bool IsWished, ProductReviews Reviews)
{
    public bool IsPreorder => Product.IsPreorder(Today);
}

public class HomeViewModel
{
    public required IReadOnlyList<ProductCard> NewArrivals { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlySet<int> WishedIds { get; init; } = new HashSet<int>();
}

public class WishlistViewModel
{
    public required PagedResult<ProductCard> Result { get; init; }
    public DateOnly Today { get; init; }
}
