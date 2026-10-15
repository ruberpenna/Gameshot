using System.Security.Cryptography;
using GameShot.Web.Data;
using GameShot.Web.Models.Ordering;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public record OrderResult(bool Success, string Message, string? OrderNumber = null)
{
    public static OrderResult Ok(string message, string orderNumber) => new(true, message, orderNumber);
    public static OrderResult Fail(string message) => new(false, message);
}

public record AdminOrderFilter
{
    public OrderStatus? Status { get; init; }
    /// <summary>주문번호·받는 분·연락처·회원 이메일 검색</summary>
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
}

public record AdminOrderRow(
    string OrderNumber,
    DateTime CreatedAt,
    OrderStatus Status,
    string? CustomerEmail,
    string RecipientName,
    string FirstItemTitle,
    int ItemCount,
    int Total);

public record OrderSummary(
    string OrderNumber,
    DateTime CreatedAt,
    OrderStatus Status,
    int Total,
    string FirstItemTitle,
    string? ThumbnailPath,
    int ItemCount);

/// <summary>
/// 주문 생성과 상태 변경.
/// 주문서를 제출하면 그 자리에서 재고를 잡고(PendingPayment), 결제 실패·취소 때 되돌린다.
/// 결제는 가상 결제라 실제 PG 연동 없이 성공/실패만 고른다.
/// </summary>
public class OrderService(ApplicationDbContext db, CartService carts, TimeProvider clock)
{
    public static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "결제 대기",
        OrderStatus.Paid => "결제 완료",
        OrderStatus.Preparing => "배송 준비중",
        OrderStatus.Shipped => "배송중",
        OrderStatus.Delivered => "배송 완료",
        OrderStatus.Cancelled => "주문 취소",
        _ => status.ToString(),
    };

    /// <param name="expectedTotal">주문서 화면에 보여준 결제 금액. 그사이 가격이 바뀌었으면 주문하지 않는다.</param>
    public async Task<OrderResult> PlaceOrderAsync(string userId, ShippingInfo shipping, int expectedTotal, CancellationToken ct = default)
    {
        var cart = await carts.GetViewAsync(new CartOwner(userId, null), ct);
        if (cart.Lines.Count == 0)
        {
            return OrderResult.Fail("장바구니가 비어 있습니다.");
        }
        if (!cart.CanCheckout)
        {
            return OrderResult.Fail("주문할 수 없는 상품이 있습니다. 장바구니를 확인해 주세요.");
        }
        if (cart.Total != expectedTotal)
        {
            return OrderResult.Fail("주문하는 사이 가격이 바뀌었습니다. 금액을 다시 확인해 주세요.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // 재고 차감: "남은 재고 >= 주문 수량"일 때만 줄여서 동시에 주문해도 음수가 되지 않게 한다.
            // 교착을 피하려고 항상 같은 순서(SKU Id 순)로 잠근다.
            foreach (var line in cart.Lines.OrderBy(l => l.VariantId))
            {
                var updated = await db.ProductVariants
                    .Where(v => v.Id == line.VariantId && v.IsActive && v.Stock >= line.Quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, v => v.Stock - line.Quantity), ct);
                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);
                    return OrderResult.Fail($"'{line.ProductTitle}' 재고가 부족해 주문할 수 없습니다.");
                }
            }

            var order = new Order
            {
                OrderNumber = NewOrderNumber(),
                UserId = userId,
                Subtotal = cart.Subtotal,
                ShippingFee = cart.ShippingFee,
                Total = cart.Total,
                Shipping = shipping,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };
            foreach (var line in cart.Lines)
            {
                order.Items.Add(new OrderItem
                {
                    ProductVariantId = line.VariantId,
                    ProductTitle = line.ProductTitle,
                    ProductSlug = line.ProductSlug,
                    ThumbnailPath = line.ThumbnailPath,
                    PlatformName = line.PlatformName,
                    Edition = line.Edition,
                    Language = line.Language,
                    Condition = line.Condition,
                    ListPrice = line.ListPrice,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Quantity,
                    LineTotal = line.LineTotal,
                });
            }
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            // 주문한 상품만 장바구니에서 뺀다.
            // ExecuteDelete는 변경 추적을 거치지 않아, 같은 DbContext에 남은 장바구니 객체가 낡은 상태가 되므로 추적을 통해 지운다
            var orderedItemIds = cart.Lines.Select(l => l.ItemId).ToList();
            var orderedItems = await db.CartItems
                .Where(i => i.Cart.UserId == userId && orderedItemIds.Contains(i.Id))
                .ToListAsync(ct);
            db.CartItems.RemoveRange(orderedItems);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return OrderResult.Ok("주문서가 접수되었습니다. 결제를 진행해 주세요.", order.OrderNumber);
        });
    }

    /// <summary>가상 결제 성공</summary>
    public async Task<OrderResult> CompletePaymentAsync(string userId, string orderNumber, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // 상태 조건을 걸고 한 번에 바꿔서, 두 번 눌러도 한 번만 처리되게 한다
        var updated = await db.Orders
            .Where(o => o.UserId == userId && o.OrderNumber == orderNumber && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, now), ct);

        return updated == 1
            ? OrderResult.Ok("결제가 완료되었습니다.", orderNumber)
            : OrderResult.Fail("결제할 수 있는 주문이 아닙니다.");
    }

    /// <summary>가상 결제 실패: 주문을 취소하고 재고를 되돌린다</summary>
    public Task<OrderResult> FailPaymentAsync(string userId, string orderNumber, CancellationToken ct = default) =>
        CancelCoreAsync(userId, orderNumber, [OrderStatus.PendingPayment], "결제에 실패해 주문이 취소되었습니다.", ct);

    /// <summary>고객 취소 (배송 준비 전까지)</summary>
    public Task<OrderResult> CancelAsync(string userId, string orderNumber, CancellationToken ct = default) =>
        CancelCoreAsync(userId, orderNumber, [OrderStatus.PendingPayment, OrderStatus.Paid], "주문을 취소했습니다.", ct);

    public Task<Order?> FindForUserAsync(string userId, string orderNumber, CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items.OrderBy(i => i.Id))
            .FirstOrDefaultAsync(o => o.UserId == userId && o.OrderNumber == orderNumber, ct);

    public Task<List<OrderSummary>> ListForUserAsync(string userId, CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderSummary(
                o.OrderNumber,
                o.CreatedAt,
                o.Status,
                o.Total,
                o.Items.OrderBy(i => i.Id).Select(i => i.ProductTitle).FirstOrDefault() ?? "",
                o.Items.OrderBy(i => i.Id).Select(i => i.ThumbnailPath).FirstOrDefault(),
                o.Items.Count))
            .ToListAsync(ct);

    /// <summary>주문서 기본값용: 가장 최근 주문의 배송지</summary>
    public async Task<ShippingInfo?> GetLastShippingAsync(string userId, CancellationToken ct = default) =>
        await db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new ShippingInfo
            {
                RecipientName = o.Shipping.RecipientName,
                Phone = o.Shipping.Phone,
                PostalCode = o.Shipping.PostalCode,
                Address1 = o.Shipping.Address1,
                Address2 = o.Shipping.Address2,
                Memo = o.Shipping.Memo,
            })
            .FirstOrDefaultAsync(ct);

    // ---- 관리자 ----

    /// <summary>관리자가 취소할 수 있는 상태 (발송 전까지)</summary>
    public static readonly OrderStatus[] AdminCancellable = [OrderStatus.PendingPayment, OrderStatus.Paid, OrderStatus.Preparing];

    /// <summary>관리자가 다음 단계로 넘길 수 있는 상태와 그 다음 상태</summary>
    public static OrderStatus? NextStatus(OrderStatus status) => status switch
    {
        OrderStatus.Paid => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.Shipped,
        OrderStatus.Shipped => OrderStatus.Delivered,
        _ => null,
    };

    /// <summary>결제 완료 → 배송 준비</summary>
    public async Task<OrderResult> StartPreparingAsync(string orderNumber, CancellationToken ct = default)
    {
        var updated = await db.Orders
            .Where(o => o.OrderNumber == orderNumber && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Preparing), ct);
        return updated == 1
            ? OrderResult.Ok("배송 준비중으로 바꿨습니다.", orderNumber)
            : OrderResult.Fail("결제 완료 상태인 주문만 배송 준비로 바꿀 수 있습니다.");
    }

    /// <summary>배송 준비 → 발송 (택배사·송장번호 필수)</summary>
    public async Task<OrderResult> ShipAsync(string orderNumber, string? courier, string? trackingNumber, CancellationToken ct = default)
    {
        courier = courier?.Trim();
        trackingNumber = trackingNumber?.Trim();
        if (string.IsNullOrEmpty(courier) || string.IsNullOrEmpty(trackingNumber))
        {
            return OrderResult.Fail("택배사와 송장번호를 입력하세요.");
        }
        if (courier.Length > 30 || trackingNumber.Length > 30)
        {
            return OrderResult.Fail("택배사·송장번호는 30자 이내로 입력하세요.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.Orders
            .Where(o => o.OrderNumber == orderNumber && o.Status == OrderStatus.Preparing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Shipped)
                .SetProperty(o => o.Courier, courier)
                .SetProperty(o => o.TrackingNumber, trackingNumber)
                .SetProperty(o => o.ShippedAt, now), ct);
        return updated == 1
            ? OrderResult.Ok("발송 처리했습니다.", orderNumber)
            : OrderResult.Fail("배송 준비중인 주문만 발송 처리할 수 있습니다.");
    }

    /// <summary>발송 → 배송 완료</summary>
    public async Task<OrderResult> MarkDeliveredAsync(string orderNumber, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.Orders
            .Where(o => o.OrderNumber == orderNumber && o.Status == OrderStatus.Shipped)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Delivered)
                .SetProperty(o => o.DeliveredAt, now), ct);
        return updated == 1
            ? OrderResult.Ok("배송 완료로 바꿨습니다.", orderNumber)
            : OrderResult.Fail("배송중인 주문만 배송 완료로 바꿀 수 있습니다.");
    }

    /// <summary>관리자 취소 (발송 전까지, 재고 복구)</summary>
    public Task<OrderResult> AdminCancelAsync(string orderNumber, CancellationToken ct = default) =>
        CancelCoreAsync(null, orderNumber, AdminCancellable, "주문을 취소하고 재고를 되돌렸습니다.", ct);

    public Task<Order?> FindForAdminAsync(string orderNumber, CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);

    public async Task<PagedResult<AdminOrderRow>> SearchForAdminAsync(AdminOrderFilter filter, CancellationToken ct = default)
    {
        const int pageSize = 20;
        var orders = db.Orders.AsNoTracking();
        if (filter.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var q = filter.Q.Trim();
            orders = orders.Where(o => o.OrderNumber.Contains(q)
                || o.Shipping.RecipientName.Contains(q)
                || o.Shipping.Phone.Contains(q)
                || (o.User.Email != null && o.User.Email.Contains(q)));
        }

        var total = await orders.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderRow(
                o.OrderNumber,
                o.CreatedAt,
                o.Status,
                o.User.Email,
                o.Shipping.RecipientName,
                o.Items.OrderBy(i => i.Id).Select(i => i.ProductTitle).FirstOrDefault() ?? "",
                o.Items.Count,
                o.Total))
            .ToListAsync(ct);
        return new PagedResult<AdminOrderRow>(rows, page, pageSize, total);
    }

    /// <param name="userId">회원 본인 취소면 회원 Id, 관리자 취소면 null</param>
    private async Task<OrderResult> CancelCoreAsync(string? userId, string orderNumber, OrderStatus[] cancellable,
        string message, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;

            var orderId = await db.Orders
                .Where(o => o.OrderNumber == orderNumber && (userId == null || o.UserId == userId))
                .Select(o => (int?)o.Id)
                .FirstOrDefaultAsync(ct);
            // 상태 조건부 변경이 성공한 요청만 재고를 되돌린다 (중복 취소 방지)
            var updated = orderId is null ? 0 : await db.Orders
                .Where(o => o.Id == orderId && cancellable.Contains(o.Status))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Cancelled)
                    .SetProperty(o => o.CancelledAt, now), ct);
            if (updated == 0)
            {
                await tx.RollbackAsync(ct);
                return OrderResult.Fail("취소할 수 없는 주문입니다.");
            }

            var items = await db.OrderItems
                .Where(i => i.OrderId == orderId && i.ProductVariantId != null)
                .Select(i => new { VariantId = i.ProductVariantId!.Value, i.Quantity })
                .ToListAsync(ct);
            foreach (var item in items.OrderBy(i => i.VariantId))
            {
                await db.ProductVariants
                    .Where(v => v.Id == item.VariantId)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, v => v.Stock + item.Quantity), ct);
            }

            await tx.CommitAsync(ct);
            return OrderResult.Ok(message, orderNumber);
        });
    }

    private string NewOrderNumber()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var suffix = RandomNumberGenerator.GetString(alphabet, 6);
        return $"GS{KoreanTime.Now(clock):yyyyMMdd}-{suffix}";
    }
}
