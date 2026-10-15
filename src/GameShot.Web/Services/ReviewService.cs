using GameShot.Web.Data;
using GameShot.Web.Models.Community;
using GameShot.Web.Models.Ordering;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public record ReviewRow(
    int Id,
    string AuthorName,
    bool IsMine,
    int Rating,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? PurchasedOption);

public record RatingSummary(double Average, int Count, IReadOnlyDictionary<int, int> CountByRating)
{
    public static readonly RatingSummary Empty = new(0, 0, new Dictionary<int, int>());

    public int Percent(int rating) => Count == 0 ? 0 : (int)Math.Round(CountByRating.GetValueOrDefault(rating) * 100.0 / Count);
}

/// <summary>상품 상세의 리뷰 영역</summary>
public record ProductReviews(
    RatingSummary Summary,
    PagedResult<ReviewRow> Page,
    ReviewEligibility MyEligibility,
    ReviewRow? MyReview);

public enum ReviewEligibility
{
    /// <summary>로그인 안 함</summary>
    NotSignedIn,
    /// <summary>이 상품을 사서 배송 완료된 적이 없음</summary>
    NotPurchased,
    CanWrite,
    /// <summary>이미 썼음 (수정·삭제 가능)</summary>
    AlreadyWritten,
}

public record AdminReviewRow(int Id, string ProductTitle, string ProductSlug, string? AuthorEmail, int Rating,
    string Content, DateTime CreatedAt, bool IsHidden);

public class ReviewService(ApplicationDbContext db, TimeProvider clock)
{
    public const int PageSize = 10;
    public const int MinLength = 10;
    public const int MaxLength = 1000;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ReviewEligibility> EligibilityAsync(string? userId, int productId, CancellationToken ct = default)
    {
        if (userId is null)
        {
            return ReviewEligibility.NotSignedIn;
        }
        if (await db.Reviews.AnyAsync(r => r.UserId == userId && r.ProductId == productId, ct))
        {
            return ReviewEligibility.AlreadyWritten;
        }
        return await HasDeliveredPurchaseAsync(userId, productId, ct) ? ReviewEligibility.CanWrite : ReviewEligibility.NotPurchased;
    }

    /// <summary>이 상품의 SKU가 든 주문이 배송 완료된 적이 있는지</summary>
    private Task<bool> HasDeliveredPurchaseAsync(string userId, int productId, CancellationToken ct) =>
        db.OrderItems.AnyAsync(i =>
            i.Order.UserId == userId
            && i.Order.Status == OrderStatus.Delivered
            && i.ProductVariant != null && i.ProductVariant.ProductId == productId, ct);

    public async Task<BenefitResult> WriteAsync(string userId, int productId, int rating, string? content, CancellationToken ct = default)
    {
        if (Validate(rating, content) is { } error)
        {
            return BenefitResult.Fail(error);
        }
        switch (await EligibilityAsync(userId, productId, ct))
        {
            case ReviewEligibility.AlreadyWritten:
                return BenefitResult.Fail("이미 리뷰를 쓴 상품입니다. 쓴 리뷰를 고쳐 주세요.");
            case ReviewEligibility.NotPurchased:
                return BenefitResult.Fail("배송 완료된 주문의 상품만 리뷰를 쓸 수 있습니다.");
        }

        db.Reviews.Add(new Review
        {
            ProductId = productId,
            UserId = userId,
            Rating = rating,
            Content = content!.Trim(),
            CreatedAt = Now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return BenefitResult.Fail("이미 리뷰를 쓴 상품입니다.");
        }
        return BenefitResult.Ok("리뷰를 등록했습니다. 감사합니다!");
    }

    /// <summary>본인 리뷰만 고칠 수 있다</summary>
    public async Task<BenefitResult> EditAsync(string userId, int reviewId, int rating, string? content, CancellationToken ct = default)
    {
        if (Validate(rating, content) is { } error)
        {
            return BenefitResult.Fail(error);
        }
        var now = Now;
        var text = content!.Trim();
        var updated = await db.Reviews
            .Where(r => r.Id == reviewId && r.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Rating, rating)
                .SetProperty(r => r.Content, text)
                .SetProperty(r => r.UpdatedAt, now), ct);
        return updated == 1 ? BenefitResult.Ok("리뷰를 고쳤습니다.") : BenefitResult.Fail("고칠 수 없는 리뷰입니다.");
    }

    public async Task<BenefitResult> DeleteAsync(string userId, int reviewId, CancellationToken ct = default)
    {
        var deleted = await db.Reviews.Where(r => r.Id == reviewId && r.UserId == userId).ExecuteDeleteAsync(ct);
        return deleted == 1 ? BenefitResult.Ok("리뷰를 지웠습니다.") : BenefitResult.Fail("지울 수 없는 리뷰입니다.");
    }

    public async Task<ProductReviews> GetForProductAsync(int productId, string? userId, int page, CancellationToken ct = default)
    {
        var visible = db.Reviews.AsNoTracking().Where(r => r.ProductId == productId && !r.IsHidden);

        var counts = await visible.GroupBy(r => r.Rating).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var total = counts.Values.Sum();
        var summary = total == 0
            ? RatingSummary.Empty
            : new RatingSummary(counts.Sum(kv => kv.Key * kv.Value) / (double)total, total, counts);

        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        var rows = await Project(visible.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id), userId)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);

        // 내 리뷰는 숨겨졌어도 본인에게는 보여 준다 (고치거나 지울 수 있게)
        var mine = userId is null ? null : await Project(
            db.Reviews.AsNoTracking().Where(r => r.ProductId == productId && r.UserId == userId), userId).FirstOrDefaultAsync(ct);

        return new ProductReviews(summary, new PagedResult<ReviewRow>(rows, page, PageSize, total),
            await EligibilityAsync(userId, productId, ct), mine);
    }

    /// <summary>회원이 쓴 리뷰가 있는 상품 Id (주문 상세에서 "리뷰 쓰기/보기" 구분)</summary>
    public async Task<HashSet<int>> ReviewedProductIdsAsync(string userId, IEnumerable<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToList();
        return (await db.Reviews.Where(r => r.UserId == userId && ids.Contains(r.ProductId))
            .Select(r => r.ProductId).ToListAsync(ct)).ToHashSet();
    }

    // ---- 관리자 ----

    public async Task<PagedResult<AdminReviewRow>> ListForAdminAsync(bool? hidden, int page, CancellationToken ct = default)
    {
        const int pageSize = 30;
        var reviews = db.Reviews.AsNoTracking();
        if (hidden is { } h)
        {
            reviews = reviews.Where(r => r.IsHidden == h);
        }
        var total = await reviews.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await reviews
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new AdminReviewRow(r.Id, r.Product.Title, r.Product.Slug, r.User.Email, r.Rating, r.Content, r.CreatedAt, r.IsHidden))
            .ToListAsync(ct);
        return new PagedResult<AdminReviewRow>(rows, page, pageSize, total);
    }

    public async Task<BenefitResult> SetHiddenAsync(int reviewId, bool hidden, CancellationToken ct = default)
    {
        var updated = await db.Reviews.Where(r => r.Id == reviewId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsHidden, hidden), ct);
        return updated == 1
            ? BenefitResult.Ok(hidden ? "리뷰를 숨겼습니다." : "리뷰를 다시 보이게 했습니다.")
            : BenefitResult.Fail("리뷰가 없습니다.");
    }

    private static string? Validate(int rating, string? content)
    {
        if (rating is < 1 or > 5)
        {
            return "별점을 1~5점 중에서 골라 주세요.";
        }
        var length = content?.Trim().Length ?? 0;
        if (length < MinLength || length > MaxLength)
        {
            return $"리뷰는 {MinLength}~{MaxLength:N0}자로 써 주세요.";
        }
        return null;
    }

    private IQueryable<ReviewRow> Project(IQueryable<Review> reviews, string? userId) =>
        reviews.Select(r => new ReviewRow(
            r.Id,
            r.User.Nickname ?? (r.User.Email != null ? r.User.Email.Substring(0, 3) + "***" : "회원"),
            r.UserId == userId,
            r.Rating,
            r.Content,
            r.CreatedAt,
            r.UpdatedAt,
            // 그 회원이 산 옵션 (가장 최근 배송 완료 주문 기준)
            db.OrderItems
                .Where(i => i.Order.UserId == r.UserId && i.Order.Status == OrderStatus.Delivered
                    && i.ProductVariant != null && i.ProductVariant.ProductId == r.ProductId)
                .OrderByDescending(i => i.OrderId)
                .Select(i => i.PlatformName + " · " + i.Edition)
                .FirstOrDefault()));
}
