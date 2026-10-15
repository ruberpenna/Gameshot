using GameShot.Web.Models.Catalog;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Data;

/// <summary>
/// 개발용 기본 데이터. 플랫폼·장르는 항상 채우고, 샘플 상품은 상품이 하나도 없을 때만 넣는다.
/// (예전 ASP 버전의 상품 목록을 그대로 옮김)
/// </summary>
public static class CatalogSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var platforms = new (string Code, string Name)[]
        {
            ("ps5", "PlayStation 5"),
            ("ps4", "PlayStation 4"),
            ("switch2", "Nintendo Switch 2"),
            ("switch", "Nintendo Switch"),
            ("xbox", "Xbox Series X|S"),
        };
        for (var i = 0; i < platforms.Length; i++)
        {
            var (code, name) = platforms[i];
            if (!await db.Platforms.AnyAsync(p => p.Code == code, ct))
            {
                db.Platforms.Add(new Platform { Code = code, Name = name, SortOrder = i });
            }
        }

        string[] genres = ["액션", "어드벤처", "RPG", "스포츠", "리듬·음악", "슈팅", "레이싱", "퍼즐"];
        for (var i = 0; i < genres.Length; i++)
        {
            var name = genres[i];
            if (!await db.Genres.AnyAsync(g => g.Name == name, ct))
            {
                db.Genres.Add(new Genre { Name = name, SortOrder = i });
            }
        }
        await db.SaveChangesAsync(ct);

        if (await db.Products.AnyAsync(ct))
        {
            return;
        }

        var ps4 = await db.Platforms.SingleAsync(p => p.Code == "ps4", ct);
        var genreByName = await db.Genres.ToDictionaryAsync(g => g.Name, ct);

        Product Make(string title, string slug, string genre, string publisher, DateOnly release, string summary,
            string edition, string language, int listPrice, int salePrice, int stock)
        {
            var product = new Product
            {
                Title = title,
                Slug = slug,
                Publisher = publisher,
                ReleaseDate = release,
                Summary = summary,
                ThumbnailPath = $"/images/products/{slug}.jpg",
                Genre = genreByName[genre],
            };
            product.Variants.Add(new ProductVariant
            {
                Platform = ps4,
                Sku = $"PS4-{slug.ToUpperInvariant()}-NEW",
                Edition = edition,
                Language = language,
                ListPrice = listPrice,
                SalePrice = salePrice,
                Stock = stock,
            });
            return product;
        }

        var farCry = Make("파 크라이 5", "far-cry-5-deluxe", "슈팅", "Ubisoft", new DateOnly(2018, 3, 27),
            "미국 몬태나 시골을 무대로 한 오픈월드 FPS", "디럭스 에디션", "한글판", 76000, 76000, 0);
        farCry.Images.Add(new ProductImage { Path = "/images/products/far-cry-5-deluxe-info1.jpg", SortOrder = 0 });
        farCry.Images.Add(new ProductImage { Path = "/images/products/far-cry-5-deluxe-info2.jpg", SortOrder = 1 });

        db.Products.AddRange(
            farCry,
            Make("파이널 판타지 15", "final-fantasy-15-royal", "RPG", "Square Enix", new DateOnly(2018, 3, 6),
                "모든 DLC가 포함된 로열 에디션", "로열 에디션", "한글판", 59400, 59400, 12),
            Make("북두와 같이", "yakuza-fist-of-the-north-star", "액션", "SEGA", new DateOnly(2018, 3, 8),
                "용과 같이 스튜디오가 만든 북두의 권 액션 어드벤처", "일반판", "한글판", 69800, 60300, 8),
            Make("MLB The Show 18", "mlb-the-show-18-mvp", "스포츠", "Sony Interactive Entertainment", new DateOnly(2018, 3, 27),
                "메이저리그 공식 라이선스 야구 게임", "MVP 에디션", "영문판", 69800, 69800, 5),
            Make("아이돌마스터 스텔라 스테이지", "idolmaster-stella-stage", "리듬·음악", "Bandai Namco", new DateOnly(2017, 12, 21),
                "765 프로덕션 아이돌을 프로듀스하는 리듬 게임", "일반판", "한글판", 65900, 60900, 3));

        await db.SaveChangesAsync(ct);
    }
}
