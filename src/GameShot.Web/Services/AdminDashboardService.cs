using GameShot.Web.Data;
using GameShot.Web.Models.Ordering;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public record LowStockRow(int ProductId, string ProductTitle, string Sku, string PlatformName, int Stock);

public record DashboardView(
    IReadOnlyDictionary<OrderStatus, int> CountByStatus,
    int TodayOrders,
    int TodaySales,
    int MonthSales,
    IReadOnlyList<LowStockRow> LowStock,
    IReadOnlyList<AdminOrderRow> RecentOrders)
{
    public int Count(OrderStatus status) => CountByStatus.GetValueOrDefault(status);
}

public class AdminDashboardService(ApplicationDbContext db, OrderService orders, TimeProvider clock)
{
    public const int LowStockThreshold = 3;

    /// <summary>매출로 치는 상태 (결제가 끝났고 취소되지 않은 주문)</summary>
    private static readonly OrderStatus[] SalesStatuses =
        [OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Shipped, OrderStatus.Delivered];

    public async Task<DashboardView> GetAsync(CancellationToken ct = default)
    {
        var counts = await db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // 한국 날짜 기준의 오늘·이번 달 시작을 UTC로 바꿔서 비교
        var today = KoreanTime.Today(clock);
        var todayStartUtc = ToUtc(today);
        var monthStartUtc = ToUtc(new DateOnly(today.Year, today.Month, 1));

        var todayOrders = await db.Orders.CountAsync(o => o.CreatedAt >= todayStartUtc, ct);
        var todaySales = await db.Orders
            .Where(o => o.CreatedAt >= todayStartUtc && SalesStatuses.Contains(o.Status))
            .SumAsync(o => o.Total, ct);
        var monthSales = await db.Orders
            .Where(o => o.CreatedAt >= monthStartUtc && SalesStatuses.Contains(o.Status))
            .SumAsync(o => o.Total, ct);

        var lowStock = await db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && v.Product.IsPublished && v.Stock <= LowStockThreshold)
            .OrderBy(v => v.Stock)
            .ThenBy(v => v.Id)
            .Take(10)
            .Select(v => new LowStockRow(v.ProductId, v.Product.Title, v.Sku, v.Platform.Name, v.Stock))
            .ToListAsync(ct);

        var recent = await orders.SearchForAdminAsync(new AdminOrderFilter(), ct);

        return new DashboardView(counts, todayOrders, todaySales, monthSales, lowStock, recent.Items.Take(5).ToList());
    }

    private static DateTime ToUtc(DateOnly kstDate) =>
        TimeZoneInfo.ConvertTimeToUtc(kstDate.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"));
}
