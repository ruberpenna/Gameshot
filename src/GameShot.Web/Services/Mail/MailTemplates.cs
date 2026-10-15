using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using GameShot.Web.Models.Ordering;

namespace GameShot.Web.Services.Mail;

public record MailContent(string Subject, string Html, string Text);

/// <summary>
/// 메일 본문. 메일 프로그램은 CSS 지원이 제각각이라 인라인 스타일만 쓴다.
/// 사용자가 입력한 값(닉네임·주소 등)은 모두 HTML 인코딩한다.
/// </summary>
public static class MailTemplates
{
    // 한글은 그대로 두고 &lt; &gt; &amp; &quot; 같은 HTML 특수문자만 이스케이프한다
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string? s) => Encoder.Encode(s ?? "");

    public static MailContent PasswordReset(string link) => Wrap(
        "[GameShot] 비밀번호 재설정 안내",
        $"""
        <p>비밀번호 재설정을 요청하셨습니다. 아래 버튼을 눌러 새 비밀번호를 정해 주세요.</p>
        {Button("비밀번호 재설정", link)}
        <p style="color:#6b7280;font-size:13px">링크는 1시간 동안만 쓸 수 있습니다. 직접 요청하지 않았다면 이 메일은 무시하셔도 됩니다.</p>
        """,
        $"비밀번호 재설정: {link}\n링크는 1시간 동안만 쓸 수 있습니다.");

    public static MailContent EmailConfirm(string nickname, string link) => Wrap(
        "[GameShot] 이메일 인증을 완료해 주세요",
        $"""
        <p><strong>{E(nickname)}</strong>님, GameShot에 가입해 주셔서 감사합니다.</p>
        <p>아래 버튼을 눌러 이메일 인증을 마치면 주문할 수 있습니다. 링크는 1시간 동안 쓸 수 있고, 지나면 내 정보에서 다시 받을 수 있습니다.</p>
        {Button("이메일 인증하기", link)}
        """,
        $"{nickname}님, 이메일 인증 링크: {link}");

    public static MailContent OrderPaid(Order order, string detailLink) => Wrap(
        $"[GameShot] 주문 {order.OrderNumber} 결제가 완료되었습니다",
        $"""
        <p>결제가 완료되었습니다. 상품을 준비해서 보내 드릴게요.</p>
        {OrderSummary(order)}
        {Button("주문 상세 보기", detailLink)}
        """,
        $"주문 {order.OrderNumber} 결제 완료 ({order.Total:N0}원)\n{detailLink}");

    public static MailContent OrderShipped(Order order, string detailLink) => Wrap(
        $"[GameShot] 주문 {order.OrderNumber} 상품을 보냈습니다",
        $"""
        <p>주문하신 상품을 보냈습니다.</p>
        <p style="font-size:15px"><strong>{E(order.Courier)}</strong> 송장번호 <strong>{E(order.TrackingNumber)}</strong></p>
        {OrderSummary(order)}
        {Button("주문 상세 보기", detailLink)}
        """,
        $"주문 {order.OrderNumber} 발송: {order.Courier} {order.TrackingNumber}\n{detailLink}");

    public static MailContent OrderCancelled(Order order, string detailLink) => Wrap(
        $"[GameShot] 주문 {order.OrderNumber}이 취소되었습니다",
        $"""
        <p>주문이 취소되었습니다. <span style="color:#6b7280">({E(order.CancelReason)})</span></p>
        {(order.PointsUsed > 0 ? $"<p>사용하신 포인트 {order.PointsUsed:N0}P는 돌려 드렸습니다.</p>" : "")}
        {(order.CouponDiscount > 0 ? "<p>사용하신 쿠폰은 다시 쓸 수 있습니다.</p>" : "")}
        {OrderSummary(order)}
        {Button("주문 상세 보기", detailLink)}
        """,
        $"주문 {order.OrderNumber} 취소 ({order.CancelReason})\n{detailLink}");

    private static string OrderSummary(Order order)
    {
        var rows = new StringBuilder();
        foreach (var item in order.Items)
        {
            rows.Append($"""
                <tr>
                  <td style="padding:6px 0">{E(item.ProductTitle)}<br><span style="color:#6b7280;font-size:12px">{E(item.PlatformName)} · {E(item.Edition)} · {item.Quantity}개</span></td>
                  <td style="padding:6px 0;text-align:right;white-space:nowrap">{item.LineTotal:N0}원</td>
                </tr>
                """);
        }
        void Line(string label, string value) =>
            rows.Append($"""<tr><td style="padding:2px 0;color:#6b7280">{label}</td><td style="text-align:right">{value}</td></tr>""");

        rows.Append("""<tr><td colspan="2"><hr style="border:0;border-top:1px solid #e5e7eb"></td></tr>""");
        Line("배송비", order.ShippingFee == 0 ? "무료" : $"{order.ShippingFee:N0}원");
        if (order.CouponDiscount > 0)
        {
            Line("쿠폰 할인", $"-{order.CouponDiscount:N0}원");
        }
        if (order.PointsUsed > 0)
        {
            Line("포인트 사용", $"-{order.PointsUsed:N0}원");
        }
        rows.Append($"""<tr><td style="padding-top:6px"><strong>결제 금액</strong></td><td style="text-align:right;padding-top:6px"><strong>{order.Total:N0}원</strong></td></tr>""");

        return $"""
            <p style="color:#6b7280;font-size:13px">주문번호 {E(order.OrderNumber)}</p>
            <table style="width:100%;border-collapse:collapse;font-size:14px">{rows}</table>
            <p style="font-size:13px;color:#6b7280">받는 분: {E(order.Shipping.RecipientName)} · ({E(order.Shipping.PostalCode)}) {E(order.Shipping.Address1)} {E(order.Shipping.Address2)}</p>
            """;
    }

    private static string Button(string label, string link) =>
        $"""<p style="margin:24px 0"><a href="{E(link)}" style="background:#111827;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none;display:inline-block">{E(label)}</a></p>""";

    private static MailContent Wrap(string subject, string body, string text) => new(
        subject,
        $"""
        <!doctype html>
        <html lang="ko"><body style="margin:0;background:#f3f4f6;font-family:'Malgun Gothic',sans-serif;color:#111827">
          <div style="max-width:560px;margin:0 auto;padding:24px">
            <div style="background:#111827;color:#fff;padding:14px 20px;border-radius:8px 8px 0 0;font-weight:bold">GameShot</div>
            <div style="background:#fff;padding:20px;border-radius:0 0 8px 8px;line-height:1.6">{body}</div>
            <p style="color:#9ca3af;font-size:12px;text-align:center">연습용 쇼핑몰 GameShot에서 보낸 메일입니다.</p>
          </div>
        </body></html>
        """,
        text + "\n\n— GameShot (연습용 쇼핑몰)");
}
