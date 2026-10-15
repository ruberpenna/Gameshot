using System.Net;
using GameShot.Web.Data;
using GameShot.Web.Models.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameShot.Tests.Web;

public sealed class CommunityPagesTests(GameShotFactory factory) : IClassFixture<GameShotFactory>
{
    private async Task<(int Id, string Slug)> SeedProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platform = await db.Platforms.FirstOrDefaultAsync() ?? new Platform { Code = "ps5", Name = "PlayStation 5" };
        var slug = $"p-{Guid.NewGuid():N}";
        var product = new Product { Title = "통합 테스트 게임", Slug = slug };
        product.Variants.Add(new ProductVariant { Platform = platform, Sku = slug.ToUpperInvariant(), ListPrice = 10000, SalePrice = 10000, Stock = 10 });
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (product.Id, slug);
    }

    [Fact]
    public async Task 비회원의_하트는_로그인으로_보내고_찜_요청은_받지_않는다()
    {
        var (id, slug) = await SeedProductAsync();
        var client = factory.CreateNoRedirectClient();

        var page = await client.GetStringAsync($"/products/{slug}");
        Assert.Contains("로그인하고 찜하기", page);

        var post = await client.PostAsync($"/wishlist/{id}", new FormUrlEncodedContent([]));
        Assert.True(post.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task 찜을_누르면_JSON으로_상태를_돌려주고_찜_목록에_나온다()
    {
        var (id, slug) = await SeedProductAsync();
        var client = factory.CreateClient();
        await client.RegisterAsync($"wish-{Guid.NewGuid():N}@example.test", "찜" + Random.Shared.Next(100_000, 999_999));

        var html = await client.GetStringAsync($"/products/{slug}");
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/wishlist/{id}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await client.SendAsync(request);

        Assert.Equal("{\"wished\":true}", await response.TextAsync());
        var wishlist = await client.GetStringAsync("/my/wishlist");
        Assert.Contains("통합 테스트 게임", wishlist);
        Assert.Contains("찜 해제", wishlist);
    }

    [Fact]
    public async Task 사지_않은_상품에는_리뷰_폼이_없고_억지로_보내도_거부된다()
    {
        var (_, slug) = await SeedProductAsync();
        var client = factory.CreateClient();
        await client.RegisterAsync($"review-{Guid.NewGuid():N}@example.test", "리뷰" + Random.Shared.Next(100_000, 999_999));

        var page = await client.GetStringAsync($"/products/{slug}");
        Assert.Contains("구매해서 배송이 완료되면 리뷰를 쓸 수 있습니다", page);
        Assert.DoesNotContain("리뷰 쓰기</h3>", page);

        var forced = await (await client.SubmitFormAsync($"/products/{slug}", new Dictionary<string, string>
        {
            ["rating"] = "5",
            ["content"] = "사지 않았지만 억지로 쓰는 리뷰",
        }, $"/products/{slug}/reviews")).TextAsync();
        Assert.Contains("배송 완료된 주문의 상품만 리뷰를 쓸 수 있습니다.", forced);
    }
}
