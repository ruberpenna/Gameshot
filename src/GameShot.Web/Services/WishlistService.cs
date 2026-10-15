using GameShot.Web.Data;
using GameShot.Web.Models.Community;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public class WishlistService(ApplicationDbContext db, TimeProvider clock)
{
    /// <summary>찜 상태를 뒤집는다</summary>
    /// <returns>바뀐 뒤 찜 상태, 상품이 없으면 null</returns>
    public async Task<bool?> ToggleAsync(string userId, int productId, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId && p.IsPublished, ct))
        {
            return null;
        }

        var removed = await db.WishItems.Where(w => w.UserId == userId && w.ProductId == productId).ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            return false;
        }

        db.WishItems.Add(new WishItem { UserId = userId, ProductId = productId, CreatedAt = clock.GetUtcNow().UtcDateTime });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 두 번 눌러 동시에 들어온 경우: 이미 찜한 상태로 본다
            db.ChangeTracker.Clear();
        }
        return true;
    }

    /// <summary>이 상품들 중 찜한 것 (목록 화면의 하트 표시용)</summary>
    public async Task<HashSet<int>> WishedAmongAsync(string? userId, IEnumerable<int> productIds, CancellationToken ct = default)
    {
        if (userId is null)
        {
            return [];
        }
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        return (await db.WishItems
            .Where(w => w.UserId == userId && ids.Contains(w.ProductId))
            .Select(w => w.ProductId)
            .ToListAsync(ct)).ToHashSet();
    }

    /// <summary>찜한 상품 Id (최근 순)</summary>
    public Task<List<int>> ListProductIdsAsync(string userId, CancellationToken ct = default) =>
        db.WishItems
            .Where(w => w.UserId == userId && w.Product.IsPublished)
            .OrderByDescending(w => w.CreatedAt)
            .ThenByDescending(w => w.Id)
            .Select(w => w.ProductId)
            .ToListAsync(ct);

    public Task<int> CountForProductAsync(int productId, CancellationToken ct = default) =>
        db.WishItems.CountAsync(w => w.ProductId == productId, ct);
}
