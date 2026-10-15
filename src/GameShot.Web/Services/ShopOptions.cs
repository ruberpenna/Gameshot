namespace GameShot.Web.Services;

/// <summary>appsettings의 "Shop" 섹션</summary>
public class ShopOptions
{
    public const string Section = "Shop";

    /// <summary>주문서 제출 후 이 시간 안에 결제하지 않으면 주문을 취소하고 재고를 되돌린다</summary>
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>이 기간 동안 손대지 않은 비회원 장바구니는 지운다 (쿠키 수명과 같게)</summary>
    public TimeSpan GuestCartRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>배송 완료 때 적립하는 포인트 비율 (배송비 제외 실결제액 기준)</summary>
    public decimal PointEarnRate { get; set; } = 0.01m;

    /// <summary>정리 작업 실행 간격</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);
}
