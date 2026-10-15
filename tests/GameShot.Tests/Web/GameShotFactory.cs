using System.Net;
using System.Text.RegularExpressions;
using GameShot.Web.Data;
using GameShot.Web.Models.Mail;
using GameShot.Web.Services.Mail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameShot.Tests.Web;

/// <summary>
/// 실제 앱을 메모리에서 띄우는 통합 테스트용 팩토리. DB만 SQLite 메모리 DB로 바꾼다.
/// 앱이 시작하면서 관리자 역할 등을 DB에 쓰므로, 시작 전에 스키마를 먼저 만들어 둔다.
/// </summary>
public sealed class GameShotFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public GameShotFactory()
    {
        _connection.Open();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development가 아니라서 user-secrets(실제 DB 연결 문자열)와 샘플 데이터 넣기를 쓰지 않는다
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=not-used");
        builder.UseSetting("Shop:MaintenanceInterval", "01:00:00");
        // 메일은 아웃박스(DB)에 쌓인 것만 확인하고 실제로는 보내지 않는다
        builder.UseSetting("Mail:WorkerInterval", "01:00:00");
        builder.UseSetting("Mail:PublicBaseUrl", "http://localhost");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
            services.RemoveAll<IMailTransport>();
            services.AddSingleton<IMailTransport>(new FakeMailTransport());
        });
    }

    /// <summary>이 주소로 마지막에 쌓인 메일</summary>
    public async Task<EmailMessage?> LastMailToAsync(string to, string kind)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.EmailMessages.AsNoTracking()
            .Where(m => m.ToAddress == to && m.Kind == kind)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<int> MailCountAsync(string to)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().EmailMessages.CountAsync(m => m.ToAddress == to);
    }

    /// <summary>메일 본문의 버튼 링크를 경로+쿼리로 (테스트 서버 주소와 상관없이 따라가도록)</summary>
    public static string LinkIn(EmailMessage mail)
    {
        var match = Regex.Match(mail.HtmlBody, "href=\"(http://localhost[^\"]+)\"");
        Assert.True(match.Success, "메일에 링크가 없습니다.");
        var uri = new Uri(WebUtility.HtmlDecode(match.Groups[1].Value));
        return uri.PathAndQuery;
    }

    /// <summary>리다이렉트를 따라가지 않는 클라이언트 (Location 헤더를 직접 확인할 때)</summary>
    public HttpClient CreateNoRedirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public static partial class HttpClientFormExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    /// <summary>폼 페이지를 열어 CSRF 토큰을 얻은 뒤 같은 쿠키로 제출한다</summary>
    public static async Task<HttpResponseMessage> SubmitFormAsync(
        this HttpClient client, string pageUrl, IDictionary<string, string> fields, string? postUrl = null)
    {
        var page = await client.GetStringAsync(pageUrl);
        var token = TokenPattern().Match(page).Groups[1].Value;
        var body = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) };
        return await client.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(body));
    }

    public static Task<HttpResponseMessage> RegisterAsync(this HttpClient client, string email, string nickname,
        string password = "gameshot123", bool agree = true) =>
        client.SubmitFormAsync("/Identity/Account/Register", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Nickname"] = nickname,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["Input.AgreeTerms"] = agree ? "true" : "false",
        });

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password,
        string? returnUrl = null) =>
        client.SubmitFormAsync("/Identity/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
        }, returnUrl is null ? null : $"/Identity/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");

    public static async Task<HttpResponseMessage> LogoutAsync(this HttpClient client) =>
        await client.SubmitFormAsync("/", new Dictionary<string, string>(), "/Identity/Account/Logout");

    public static Task<string> TextAsync(this HttpResponseMessage response) => response.Content.ReadAsStringAsync();
}
