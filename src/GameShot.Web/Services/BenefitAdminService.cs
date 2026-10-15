using System.ComponentModel.DataAnnotations;
using GameShot.Web.Data;
using GameShot.Web.Models.Benefits;
using Microsoft.EntityFrameworkCore;

namespace GameShot.Web.Services;

public class CouponForm : IValidatableObject
{
    [Display(Name = "쿠폰 코드")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [StringLength(30, MinimumLength = 4, ErrorMessage = "{0}는 {2}~{1}자로 입력하세요.")]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "영문·숫자·하이픈만 쓸 수 있습니다.")]
    public string Code { get; set; } = "";

    [Display(Name = "쿠폰 이름")]
    [Required(ErrorMessage = "{0}을 입력하세요.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Display(Name = "할인 방식")]
    public DiscountType DiscountType { get; set; } = DiscountType.Amount;

    [Display(Name = "할인 값")]
    [Range(1, 10_000_000, ErrorMessage = "{0}은 1 이상으로 입력하세요.")]
    public int DiscountValue { get; set; }

    [Display(Name = "최대 할인액")]
    [Range(1, 10_000_000, ErrorMessage = "{0}은 1 이상으로 입력하세요.")]
    public int? MaxDiscount { get; set; }

    [Display(Name = "최소 주문 금액")]
    [Range(0, 100_000_000, ErrorMessage = "{0}은 0 이상으로 입력하세요.")]
    public int MinOrderAmount { get; set; }

    /// <summary>한국 시간으로 입력받는다</summary>
    [Display(Name = "사용 시작")]
    public DateTime? StartsAtKst { get; set; }

    [Display(Name = "사용 종료")]
    public DateTime? EndsAtKst { get; set; }

    [Display(Name = "발급 수 제한")]
    [Range(1, 10_000_000, ErrorMessage = "{0}은 1 이상으로 입력하세요.")]
    public int? IssueLimit { get; set; }

    [Display(Name = "사용 가능")]
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (DiscountType == DiscountType.Percent && DiscountValue > 100)
        {
            yield return new ValidationResult("정률 할인은 100% 이하로 입력하세요.", [nameof(DiscountValue)]);
        }
        if (StartsAtKst is { } s && EndsAtKst is { } e && e <= s)
        {
            yield return new ValidationResult("종료 시각은 시작 시각보다 뒤여야 합니다.", [nameof(EndsAtKst)]);
        }
    }

    public static CouponForm From(Coupon c) => new()
    {
        Code = c.Code,
        Name = c.Name,
        DiscountType = c.DiscountType,
        DiscountValue = c.DiscountValue,
        MaxDiscount = c.MaxDiscount,
        MinOrderAmount = c.MinOrderAmount,
        StartsAtKst = c.StartsAt is { } s ? KoreanTime.FromUtc(s) : null,
        EndsAtKst = c.EndsAt is { } e ? KoreanTime.FromUtc(e) : null,
        IssueLimit = c.IssueLimit,
        IsActive = c.IsActive,
    };
}

public record AdminCouponRow(int Id, string Code, string Name, string DiscountText, int MinOrderAmount,
    DateTime? EndsAt, int? IssueLimit, int IssuedCount, int UsedCount, bool IsActive);

public record AdminMemberRow(string Id, string? Email, DateTime CreatedAt, int PointBalance, int OrderCount);

/// <summary>관리자 쿠폰 관리와 회원 포인트 조회</summary>
public class BenefitAdminService(ApplicationDbContext db)
{
    public Task<List<AdminCouponRow>> ListCouponsAsync(CancellationToken ct = default) =>
        db.Coupons
            .AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Select(c => new AdminCouponRow(c.Id, c.Code, c.Name,
                c.DiscountType == DiscountType.Percent ? c.DiscountValue + "%" : c.DiscountValue + "원",
                c.MinOrderAmount, c.EndsAt, c.IssueLimit, c.Issued.Count, c.Issued.Count(i => i.UsedAt != null), c.IsActive))
            .ToListAsync(ct);

    public Task<Coupon?> GetCouponAsync(int id, CancellationToken ct = default) =>
        db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<AdminResult> SaveCouponAsync(int? id, CouponForm form, CancellationToken ct = default)
    {
        var code = form.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code && c.Id != id, ct))
        {
            return AdminResult.Fail($"쿠폰 코드 '{code}'는 이미 있습니다.");
        }

        Coupon? coupon;
        if (id is null)
        {
            coupon = new Coupon { Code = code, Name = "" };
            db.Coupons.Add(coupon);
        }
        else
        {
            coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (coupon is null)
            {
                return AdminResult.Fail("쿠폰이 없습니다.");
            }
        }

        coupon.Code = code;
        coupon.Name = form.Name.Trim();
        coupon.DiscountType = form.DiscountType;
        coupon.DiscountValue = form.DiscountValue;
        coupon.MaxDiscount = form.DiscountType == DiscountType.Percent ? form.MaxDiscount : null;
        coupon.MinOrderAmount = form.MinOrderAmount;
        coupon.StartsAt = ToUtc(form.StartsAtKst);
        coupon.EndsAt = ToUtc(form.EndsAtKst);
        coupon.IssueLimit = form.IssueLimit;
        coupon.IsActive = form.IsActive;
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok(id is null ? "쿠폰을 만들었습니다." : "쿠폰을 저장했습니다.", coupon.Id);
    }

    public async Task<PagedResult<AdminMemberRow>> SearchMembersAsync(string? q, int page, CancellationToken ct = default)
    {
        const int pageSize = 30;
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            users = users.Where(u => u.Email != null && u.Email.Contains(term));
        }
        var total = await users.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminMemberRow(u.Id, u.Email, u.CreatedAt, u.PointBalance,
                db.Orders.Count(o => o.UserId == u.Id)))
            .ToListAsync(ct);
        return new PagedResult<AdminMemberRow>(rows, page, pageSize, total);
    }

    public Task<AdminMemberRow?> GetMemberAsync(string id, CancellationToken ct = default) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new AdminMemberRow(u.Id, u.Email, u.CreatedAt, u.PointBalance,
                db.Orders.Count(o => o.UserId == u.Id)))
            .FirstOrDefaultAsync(ct);

    private static DateTime? ToUtc(DateTime? kst) =>
        kst is { } t
            ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"))
            : null;
}
