using System.Net;
using System.Text.RegularExpressions;
using GameShot.Web.Data;
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
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        });
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
