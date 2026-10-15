using GameShot.Web.Models.Benefits;

namespace GameShot.Web.Services;

/// <summary>주문 금액 내역. 배송비 무료 기준은 할인 전 상품 금액으로 본다.</summary>
public record PriceBreakdown(int Subtotal, int ShippingFee, int CouponDiscount, int PointsUsed)
{
    public int Total => Subtotal + ShippingFee - CouponDiscount - PointsUsed;

    /// <summary>포인트 적립 기준 금액 (배송비 제외 실결제액)</summary>
    public int EarnBase => Subtotal - CouponDiscount - PointsUsed;
}

/// <summary>할인 계산 규칙 (DB에 의존하지 않는 순수 계산)</summary>
public static class PriceCalculator
{
    /// <summary>
    /// 쿠폰 할인액. 최소 주문 금액에 못 미치면 0.
    /// 정률은 10원 단위로 버리고 최대 할인액을 넘지 않으며, 어떤 경우에도 상품 금액을 넘지 않는다.
    /// </summary>
    public static int CouponDiscount(Coupon coupon, int subtotal)
    {
        if (subtotal <= 0 || subtotal < coupon.MinOrderAmount)
        {
            return 0;
        }

        var discount = coupon.DiscountType switch
        {
            DiscountType.Percent => (int)((long)subtotal * coupon.DiscountValue / 100 / 10 * 10),
            _ => coupon.DiscountValue,
        };
        if (coupon.MaxDiscount is { } max)
        {
            discount = Math.Min(discount, max);
        }
        return Math.Clamp(discount, 0, subtotal);
    }

    /// <summary>쓸 수 있는 최대 포인트: 잔액과 (상품 금액 - 쿠폰 할인) 중 작은 값. 배송비는 포인트로 낼 수 없다.</summary>
    public static int MaxUsablePoints(int subtotal, int couponDiscount, int balance) =>
        Math.Max(0, Math.Min(balance, subtotal - couponDiscount));

    /// <summary>적립 포인트 (원 단위 버림)</summary>
    public static int EarnedPoints(int earnBase, decimal rate) =>
        earnBase <= 0 ? 0 : (int)Math.Floor(earnBase * rate);
}
