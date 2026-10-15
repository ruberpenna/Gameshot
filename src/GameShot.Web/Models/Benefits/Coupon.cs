namespace GameShot.Web.Models.Benefits;

public enum DiscountType
{
    /// <summary>정액 (원)</summary>
    Amount = 0,
    /// <summary>정률 (%)</summary>
    Percent = 1,
}

/// <summary>관리자가 만드는 쿠폰 정의. 회원에게는 <see cref="UserCoupon"/>으로 한 장씩 발급된다.</summary>
public class Coupon
{
    public int Id { get; set; }

    /// <summary>회원이 쿠폰함에 등록할 때 입력하는 코드 (대문자)</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public DiscountType DiscountType { get; set; }

    /// <summary>정액이면 원, 정률이면 %</summary>
    public int DiscountValue { get; set; }

    /// <summary>정률 쿠폰의 최대 할인액 (없으면 제한 없음)</summary>
    public int? MaxDiscount { get; set; }

    /// <summary>상품 금액이 이 이상일 때만 쓸 수 있다</summary>
    public int MinOrderAmount { get; set; }

    /// <summary>사용 시작·종료 (UTC, null이면 제한 없음)</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>전체 발급 수 제한 (null이면 무제한)</summary>
    public int? IssueLimit { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserCoupon> Issued { get; set; } = [];

    public bool IsUsableAt(DateTime utcNow) =>
        IsActive && (StartsAt is null || StartsAt <= utcNow) && (EndsAt is null || utcNow < EndsAt);

    public string DiscountText => DiscountType == DiscountType.Percent
        ? $"{DiscountValue}% 할인" + (MaxDiscount is { } max ? $" (최대 {max:N0}원)" : "")
        : $"{DiscountValue:N0}원 할인";
}

/// <summary>회원 쿠폰함의 쿠폰 한 장. 쿠폰 하나당 회원에게 한 장만 발급된다.</summary>
public class UserCoupon
{
    public int Id { get; set; }

    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>사용한 시각. 주문이 취소되면 다시 null로 돌아간다.</summary>
    public DateTime? UsedAt { get; set; }

    public int? UsedOrderId { get; set; }
}
