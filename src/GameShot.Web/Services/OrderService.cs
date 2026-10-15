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

            // 주문한 상품만 장바구니에서 뺀다
            var orderedItemIds = cart.Lines.Select(l => l.ItemId).ToList();
            await db.CartItems
                .Where(i => i.Cart.UserId == userId && orderedItemIds.Contains(i.Id))
                .ExecuteDeleteAsync(ct);

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

    private async Task<OrderResult> CancelCoreAsync(string userId, string orderNumber, OrderStatus[] cancellable,
        string message, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;

            var orderId = await db.Orders
                .Where(o => o.UserId == userId && o.OrderNumber == orderNumber)
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
