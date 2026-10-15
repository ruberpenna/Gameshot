using GameShot.Web.Data;
using GameShot.Web.Models.Benefits;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public record BenefitResult(bool Success, string Message)
{
    public static BenefitResult Ok(string message) => new(true, message);
    public static BenefitResult Fail(string message) => new(false, message);
}

/// <summary>쿠폰함의 쿠폰 한 장 (화면용)</summary>
public record WalletCoupon(
    int UserCouponId,
    string Name,
    string DiscountText,
    int MinOrderAmount,
    DateTime? EndsAt,
    DateTime IssuedAt,
    DateTime? UsedAt,
    bool IsUsableNow);

/// <summary>주문서의 쿠폰 선택지</summary>
public record CouponOption(int UserCouponId, string Name, string DiscountText, int Discount, string? UnavailableReason)
{
    public bool IsApplicable => UnavailableReason is null;
}

/// <summary>
/// 쿠폰 발급·조회와 포인트 잔액·내역.
/// 주문에 쓰고 되돌리는 메서드(Use*/Restore*/Earn*)는 OrderService가 연 트랜잭션 안에서 같은 DbContext로 호출한다.
/// </summary>
public class BenefitService(ApplicationDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---- 쿠폰 ----

    /// <summary>회원이 쿠폰 코드를 입력해 쿠폰함에 넣는다</summary>
    public async Task<BenefitResult> RegisterCodeAsync(string userId, string? code, CancellationToken ct = default)
    {
        code = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return BenefitResult.Fail("쿠폰 코드를 입력하세요.");
        }
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (coupon is null || !coupon.IsActive || (coupon.EndsAt is { } end && end <= Now))
        {
            return BenefitResult.Fail("사용할 수 없는 쿠폰 코드입니다.");
        }
        return await IssueAsync(coupon, userId, ct);
    }

    /// <summary>관리자가 회원(이메일)에게 직접 발급</summary>
    public async Task<BenefitResult> IssueToEmailAsync(int couponId, string? email, CancellationToken ct = default)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == couponId, ct);
        if (coupon is null)
        {
            return BenefitResult.Fail("쿠폰이 없습니다.");
        }
        var normalized = email?.Trim().ToUpperInvariant();
        var userId = await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (userId is null)
        {
            return BenefitResult.Fail("해당 이메일의 회원이 없습니다.");
        }
        return await IssueAsync(coupon, userId, ct);
    }

    private async Task<BenefitResult> IssueAsync(Coupon coupon, string userId, CancellationToken ct)
    {
        if (await db.UserCoupons.AnyAsync(u => u.CouponId == coupon.Id && u.UserId == userId, ct))
        {
            return BenefitResult.Fail("이미 받은 쿠폰입니다.");
        }
        if (coupon.IssueLimit is { } limit && await db.UserCoupons.CountAsync(u => u.CouponId == coupon.Id, ct) >= limit)
        {
            return BenefitResult.Fail("선착순 발급이 끝난 쿠폰입니다.");
        }

        db.UserCoupons.Add(new UserCoupon { CouponId = coupon.Id, UserId = userId, IssuedAt = Now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 두 번 눌러 동시에 들어온 경우: (쿠폰, 회원) 유니크 인덱스가 막는다
            db.ChangeTracker.Clear();
            return BenefitResult.Fail("이미 받은 쿠폰입니다.");
        }
        return BenefitResult.Ok($"'{coupon.Name}' 쿠폰을 받았습니다.");
    }

    public async Task<List<WalletCoupon>> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var now = Now;
        var rows = await db.UserCoupons
            .AsNoTracking()
            .Include(u => u.Coupon)
            .Where(u => u.UserId == userId)
            .OrderBy(u => u.UsedAt != null)
            .ThenByDescending(u => u.IssuedAt)
            .ToListAsync(ct);
        return rows.Select(u => new WalletCoupon(
            u.Id, u.Coupon.Name, u.Coupon.DiscountText, u.Coupon.MinOrderAmount, u.Coupon.EndsAt,
            u.IssuedAt, u.UsedAt, u.UsedAt is null && u.Coupon.IsUsableAt(now))).ToList();
    }

    /// <summary>주문서용: 아직 안 쓴 쿠폰과 이 금액에 적용했을 때 할인액(또는 못 쓰는 이유)</summary>
    public async Task<List<CouponOption>> GetOptionsAsync(string userId, int subtotal, CancellationToken ct = default)
    {
        var now = Now;
        var unused = await db.UserCoupons
            .AsNoTracking()
            .Include(u => u.Coupon)
            .Where(u => u.UserId == userId && u.UsedAt == null)
            .ToListAsync(ct);

        return unused
            .Where(u => u.Coupon.IsActive && (u.Coupon.EndsAt is null || now < u.Coupon.EndsAt))
            .Select(u => new CouponOption(u.Id, u.Coupon.Name, u.Coupon.DiscountText,
                PriceCalculator.CouponDiscount(u.Coupon, subtotal), WhyUnusable(u.Coupon, subtotal, now)))
            .OrderBy(o => o.UnavailableReason is not null)
            .ThenByDescending(o => o.Discount)
            .ToList();
    }

    private static string? WhyUnusable(Coupon coupon, int subtotal, DateTime now)
    {
        if (!coupon.IsUsableAt(now))
        {
            return coupon.StartsAt > now ? "사용 기간 전" : "사용할 수 없음";
        }
        if (subtotal < coupon.MinOrderAmount)
        {
            return $"{coupon.MinOrderAmount:N0}원 이상 주문 시";
        }
        return null;
    }

    /// <summary>주문에 쓸 쿠폰이 이 회원 것이고 지금 이 금액에 쓸 수 있는지 확인하고 할인액을 계산한다 (사용 처리는 하지 않음)</summary>
    /// <returns>(할인액, 쿠폰 이름) 또는 실패 메시지</returns>
    internal async Task<(int Discount, string? Name, string? Error)> CheckCouponAsync(
        string userId, int userCouponId, int subtotal, CancellationToken ct)
    {
        var userCoupon = await db.UserCoupons
            .AsNoTracking()
            .Include(u => u.Coupon)
            .FirstOrDefaultAsync(u => u.Id == userCouponId && u.UserId == userId, ct);
        if (userCoupon is null || userCoupon.UsedAt is not null)
        {
            return (0, null, "사용할 수 없는 쿠폰입니다.");
        }
        if (WhyUnusable(userCoupon.Coupon, subtotal, Now) is { } reason)
        {
            return (0, null, $"쿠폰을 쓸 수 없습니다: {reason}");
        }
        return (PriceCalculator.CouponDiscount(userCoupon.Coupon, subtotal), userCoupon.Coupon.Name, null);
    }

    /// <summary>
    /// 쿠폰을 사용 처리한다 (주문 트랜잭션 안에서 호출). "아직 안 씀" 조건부 UPDATE라 한 장을 두 주문에 쓸 수 없다.
    /// </summary>
    internal async Task<bool> MarkCouponUsedAsync(int userCouponId, int orderId, CancellationToken ct)
    {
        var now = Now;
        var updated = await db.UserCoupons
            .Where(u => u.Id == userCouponId && u.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedAt, now).SetProperty(u => u.UsedOrderId, orderId), ct);
        return updated == 1;
    }

    /// <summary>주문 취소: 그 주문에 쓴 쿠폰을 다시 쓸 수 있게 돌려 놓는다</summary>
    internal Task RestoreCouponAsync(int orderId, CancellationToken ct) =>
        db.UserCoupons
            .Where(u => u.UsedOrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedAt, (DateTime?)null).SetProperty(u => u.UsedOrderId, (int?)null), ct);

    // ---- 포인트 ----

    public Task<int> GetBalanceAsync(string userId, CancellationToken ct = default) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.PointBalance).FirstOrDefaultAsync(ct);

    public Task<List<PointTransaction>> GetHistoryAsync(string userId, int take = 100, CancellationToken ct = default) =>
        db.PointTransactions
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>
    /// 잔액을 바꾸고 내역을 남긴다. 차감(음수)은 잔액이 충분할 때만 성공한다.
    /// </summary>
    internal async Task<bool> ChangePointsAsync(string userId, int amount, PointType type, string description,
        int? orderId, CancellationToken ct)
    {
        if (amount == 0)
        {
            return true;
        }

        var updated = await db.Users
            .Where(u => u.Id == userId && u.PointBalance + amount >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PointBalance, u => u.PointBalance + amount), ct);
        if (updated == 0)
        {
            return false;
        }

        var balance = await GetBalanceAsync(userId, ct);
        db.PointTransactions.Add(new PointTransaction
        {
            UserId = userId,
            Amount = amount,
            BalanceAfter = balance,
            Type = type,
            OrderId = orderId,
            Description = description,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>관리자 지급(+)·차감(-)</summary>
    public async Task<BenefitResult> AdjustPointsAsync(string userId, int amount, string? reason, CancellationToken ct = default)
    {
        if (amount == 0 || Math.Abs(amount) > 10_000_000)
        {
            return BenefitResult.Fail("포인트는 0이 아닌 1,000만 이하 값으로 입력하세요.");
        }
        reason = string.IsNullOrWhiteSpace(reason) ? (amount > 0 ? "관리자 지급" : "관리자 차감") : reason.Trim();
        if (reason.Length > 100)
        {
            return BenefitResult.Fail("사유는 100자 이내로 입력하세요.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await ChangePointsAsync(userId, amount, PointType.Adjust, reason, null, ct))
            {
                await tx.RollbackAsync(ct);
                return BenefitResult.Fail("잔액보다 많이 차감할 수 없습니다.");
            }
            await tx.CommitAsync(ct);
            return BenefitResult.Ok(amount > 0 ? $"{amount:N0}P를 지급했습니다." : $"{-amount:N0}P를 차감했습니다.");
        });
    }
}
