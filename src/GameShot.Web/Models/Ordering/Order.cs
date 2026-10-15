using GameShot.Web.Models.Catalog;

namespace GameShot.Web.Models.Ordering;

public enum OrderStatus
{
    /// <summary>주문서 제출 직후. 재고는 이미 잡아 둔 상태</summary>
    PendingPayment = 0,
    Paid = 1,
    Preparing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 9,
}

/// <summary>주문 시점의 배송지 (주문 테이블의 컬럼으로 저장)</summary>
public class ShippingInfo
{
    public required string RecipientName { get; set; }
    public required string Phone { get; set; }
    public required string PostalCode { get; set; }
    public required string Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Memo { get; set; }
}

public class Order
{
    public int Id { get; set; }

    /// <summary>고객에게 보여주는 주문번호 (예: GS20261015-7K3M9Q)</summary>
    public required string OrderNumber { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    public int Subtotal { get; set; }
    public int ShippingFee { get; set; }

    /// <summary>쿠폰 할인액</summary>
    public int CouponDiscount { get; set; }

    /// <summary>사용한 쿠폰 (취소하면 되돌려 준다)</summary>
    public int? UserCouponId { get; set; }

    /// <summary>주문 시점의 쿠폰 이름</summary>
    public string? CouponName { get; set; }

    /// <summary>사용한 포인트</summary>
    public int PointsUsed { get; set; }

    /// <summary>배송 완료 때 적립한 포인트</summary>
    public int PointsEarned { get; set; }

    /// <summary>실제 결제 금액 = 상품 금액 + 배송비 - 쿠폰 할인 - 포인트</summary>
    public int Total { get; set; }

    public required ShippingInfo Shipping { get; set; }

    /// <summary>결제 수단 (지금은 가상 결제만)</summary>
    public string PaymentMethod { get; set; } = "가상 결제";

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>취소 사유 (고객 취소, 결제 실패, 결제 시간 초과, 관리자 취소)</summary>
    public string? CancelReason { get; set; }

    /// <summary>택배사 (발송 처리 때 입력)</summary>
    public string? Courier { get; set; }

    /// <summary>송장번호</summary>
    public string? TrackingNumber { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];

    public bool CanPay => Status == OrderStatus.PendingPayment;

    /// <summary>배송 준비 전까지만 고객이 직접 취소할 수 있다</summary>
    public bool CanCancel => Status is OrderStatus.PendingPayment or OrderStatus.Paid;
}

/// <summary>
/// 주문 시점의 상품 정보 사본. 나중에 상품명·가격이 바뀌거나 상품이 내려가도 주문 내역은 그대로 남는다.
/// </summary>
public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>재고 복구용. 상품이 지워져도 주문은 남도록 null 허용</summary>
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public required string ProductTitle { get; set; }
    public string? ProductSlug { get; set; }
    public string? ThumbnailPath { get; set; }
    public required string PlatformName { get; set; }
    public required string Edition { get; set; }
    public required string Language { get; set; }
    public ItemCondition Condition { get; set; }

    public int ListPrice { get; set; }
    public int UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int LineTotal { get; set; }
}
