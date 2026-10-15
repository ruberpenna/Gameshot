using System.ComponentModel.DataAnnotations;
using GameShot.Web.Models.Ordering;
using GameShot.Web.Services;

namespace GameShot.Web.Models;

/// <summary>주문서에서 입력받는 배송지</summary>
public class CheckoutForm
{
    [Display(Name = "받는 분")]
    [Required(ErrorMessage = "{0}을 입력하세요.")]
    [StringLength(30, ErrorMessage = "{0}은 {1}자 이내로 입력하세요.")]
    public string RecipientName { get; set; } = "";

    [Display(Name = "연락처")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [RegularExpression(@"^0\d{1,2}-?\d{3,4}-?\d{4}$", ErrorMessage = "연락처 형식이 올바르지 않습니다. (예: 010-1234-5678)")]
    public string Phone { get; set; } = "";

    [Display(Name = "우편번호")]
    [Required(ErrorMessage = "주소 검색으로 {0}를 입력하세요.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "{0}는 숫자 5자리입니다.")]
    public string PostalCode { get; set; } = "";

    [Display(Name = "주소")]
    [Required(ErrorMessage = "{0}를 입력하세요.")]
    [StringLength(200)]
    public string Address1 { get; set; } = "";

    [Display(Name = "상세 주소")]
    [StringLength(200, ErrorMessage = "{0}는 {1}자 이내로 입력하세요.")]
    public string? Address2 { get; set; }

    [Display(Name = "배송 메모")]
    [StringLength(100, ErrorMessage = "{0}는 {1}자 이내로 입력하세요.")]
    public string? Memo { get; set; }

    /// <summary>주문서에 보여준 결제 금액 (서버에서 다시 계산한 금액과 비교)</summary>
    public int ExpectedTotal { get; set; }

    /// <summary>쓸 쿠폰 (쿠폰함의 Id, 안 쓰면 null)</summary>
    public int? UserCouponId { get; set; }

    [Display(Name = "포인트")]
    [Range(0, int.MaxValue, ErrorMessage = "{0}는 0 이상으로 입력하세요.")]
    public int PointsToUse { get; set; }

    public ShippingInfo ToShippingInfo() => new()
    {
        RecipientName = RecipientName.Trim(),
        Phone = NormalizePhone(Phone),
        PostalCode = PostalCode.Trim(),
        Address1 = Address1.Trim(),
        Address2 = string.IsNullOrWhiteSpace(Address2) ? null : Address2.Trim(),
        Memo = string.IsNullOrWhiteSpace(Memo) ? null : Memo.Trim(),
    };

    public static CheckoutForm From(ShippingInfo? s) => s is null ? new() : new()
    {
        RecipientName = s.RecipientName,
        Phone = s.Phone,
        PostalCode = s.PostalCode,
        Address1 = s.Address1,
        Address2 = s.Address2,
        Memo = s.Memo,
    };

    /// <summary>01012345678 → 010-1234-5678</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            11 => $"{digits[..3]}-{digits[3..7]}-{digits[7..]}",
            10 when digits.StartsWith("02") => $"{digits[..2]}-{digits[2..6]}-{digits[6..]}",
            10 => $"{digits[..3]}-{digits[3..6]}-{digits[6..]}",
            9 => $"{digits[..2]}-{digits[2..5]}-{digits[5..]}",
            _ => phone.Trim(),
        };
    }
}

public class CheckoutViewModel
{
    public required CheckoutForm Form { get; init; }
    public required CheckoutQuote Quote { get; init; }
    public CartView Cart => Quote.Cart;
}
