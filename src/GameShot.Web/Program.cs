using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Services;
using GameShot.Web.Services.Identity;
using GameShot.Web.Services.Mail;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 연결 문자열은 user-secrets(개발) 또는 환경 변수(운영)에서 읽는다. tools\import-connection.ps1 참고
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection 설정이 없습니다. user-secrets를 확인하세요.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    // DB 서버가 SQL Server 2019라서 호환성 수준 150에 맞춘다
    options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(150)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        // 메일 발송을 붙이기 전까지는 이메일 인증 없이 가입
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        // 8자 이상, 영문 소문자와 숫자 포함
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        // 5번 틀리면 10분 잠금
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddErrorDescriber<KoreanIdentityErrorDescriber>()
    .AddClaimsPrincipalFactory<GameShotClaimsFactory>();
builder.Services.AddScoped<MemberService>();
// 비밀번호 재설정·이메일 인증 링크 유효 시간
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<AccountMailer>();
builder.Services.AddControllersWithViews();
// 한글을 &#xAC00; 같은 엔티티로 바꾸지 않고 그대로 출력 (HTML 특수문자는 계속 이스케이프된다)
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ShopOptions>(builder.Configuration.GetSection(ShopOptions.Section));
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CartContext>();
builder.Services.Configure<MailOptions>(builder.Configuration.GetSection(MailOptions.Section));
// SMTP 설정이 있으면 실제로 보내고, 없으면 App_Data/mail에 .eml 파일로 남긴다
builder.Services.AddSingleton<IMailTransport>(sp =>
    sp.GetRequiredService<IOptions<MailOptions>>().Value.UsesSmtp
        ? ActivatorUtilities.CreateInstance<SmtpMailTransport>(sp)
        : ActivatorUtilities.CreateInstance<PickupDirectoryMailTransport>(sp));
builder.Services.AddScoped<MailQueue>();
builder.Services.AddScoped<OrderMailer>();
builder.Services.AddHostedService<MailWorker>();
builder.Services.AddScoped<BenefitService>();
builder.Services.AddScoped<BenefitAdminService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProductAdminService>();
builder.Services.AddScoped<AdminDashboardService>();
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();

// 관리자 화면 업로드 한도 (이미지 5MB + 여유)
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 6 * 1024 * 1024);

var korean = new CultureInfo("ko-KR");
CultureInfo.DefaultThreadCurrentCulture = korean;
CultureInfo.DefaultThreadCurrentUICulture = korean;

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await AdminBootstrap.RunAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await CatalogSeeder.SeedAsync(db);
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.BlockUnsupportedIdentityPages();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
// 관리자가 올린 상품 이미지 (빌드 때 없던 파일이라 MapStaticAssets가 모름)
app.UseStaticFiles();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// 통합 테스트(WebApplicationFactory)에서 참조할 수 있게
public partial class Program;
