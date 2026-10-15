namespace GameShot.Web.Models.Mail;

/// <summary>
/// 보낼 메일 (아웃박스). 업무 처리와 같은 트랜잭션에서 이 행을 넣고, 발송은 백그라운드 작업이 맡는다.
/// 그래서 "주문은 취소됐는데 메일은 안 감" 같은 어긋남이 없고, SMTP가 잠깐 안 돼도 나중에 다시 보낸다.
/// </summary>
public class EmailMessage
{
    public int Id { get; set; }

    public required string ToAddress { get; set; }

    /// <summary>메일 종류 (재설정·인증·주문 등). 같은 종류를 너무 자주 보내지 않는 데 쓴다.</summary>
    public required string Kind { get; set; }

    public required string Subject { get; set; }

    public required string HtmlBody { get; set; }

    public required string TextBody { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>실패하면 이 시각 이후에 다시 시도</summary>
    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
