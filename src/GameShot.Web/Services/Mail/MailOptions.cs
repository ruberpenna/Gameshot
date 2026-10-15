namespace GameShot.Web.Services.Mail;

/// <summary>appsettings "Mail" 섹션. SMTP 계정은 user-secrets(개발)·환경 변수(운영)에 둔다.</summary>
public class MailOptions
{
    public const string Section = "Mail";

    public string FromAddress { get; set; } = "noreply@gameshot.local";
    public string FromName { get; set; } = "GameShot";

    /// <summary>메일 속 링크의 기준 주소 (예: https://gameshot.example.com)</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5120";

    /// <summary>SMTP 호스트. 비어 있으면 실제로 보내지 않고 PickupDirectory에 파일로 남긴다.</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 465;

    /// <summary>true: 접속하자마자 SSL(465), false: STARTTLS(587)</summary>
    public bool SmtpSslOnConnect { get; set; } = true;
    public string? SmtpUserName { get; set; }
    public string? SmtpPassword { get; set; }

    /// <summary>SMTP가 없을 때 메일을 .eml 파일로 저장할 폴더 (콘텐츠 루트 기준)</summary>
    public string PickupDirectory { get; set; } = "App_Data/mail";

    /// <summary>발송 작업 주기</summary>
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>같은 주소·종류의 메일을 다시 보내기까지 기다리는 시간 (재설정·인증 메일 남용 방지)</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromMinutes(1);

    public const int MaxAttempts = 5;

    public bool UsesSmtp => !string.IsNullOrWhiteSpace(SmtpHost);
}
