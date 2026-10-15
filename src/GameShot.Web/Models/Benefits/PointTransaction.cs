namespace GameShot.Web.Models.Benefits;

public enum PointType
{
    /// <summary>구매 적립 (배송 완료 때)</summary>
    Earn = 0,
    /// <summary>주문에 사용</summary>
    Use = 1,
    /// <summary>주문 취소로 사용 포인트 반환</summary>
    Refund = 2,
    /// <summary>관리자 지급·차감</summary>
    Adjust = 3,
}

/// <summary>
/// 포인트 변동 내역. 잔액은 회원(<see cref="ApplicationUser.PointBalance"/>)에 따로 두고,
/// 잔액을 바꿀 때마다 이 내역을 함께 남긴다.
/// </summary>
public class PointTransaction
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>적립은 +, 사용은 -</summary>
    public int Amount { get; set; }

    /// <summary>이 변동 직후 잔액</summary>
    public int BalanceAfter { get; set; }

    public PointType Type { get; set; }

    public int? OrderId { get; set; }

    public required string Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
