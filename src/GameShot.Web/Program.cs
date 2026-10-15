using System.Globalization;
using GameShot.Web.Data;
using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 연결 문자열은 user-secrets(개발) 또는 환경 변수(운영)에서 읽는다. tools\import-connection.ps1 참고
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection 설정이 없습니다. user-secrets를 확인하세요.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    // DB 서버가 SQL Server 2019라서 호환성 수준 150에 맞춘다
    options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(150)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// 메일 발송을 붙이기 전까지는 이메일 인증 없이 가입
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CartContext>();
builder.Services.AddScoped<OrderService>();

var korean = new CultureInfo("ko-KR");
CultureInfo.DefaultThreadCurrentCulture = korean;
CultureInfo.DefaultThreadCurrentUICulture = korean;

var app = builder.Build();

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
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// 통합 테스트(WebApplicationFactory)에서 참조할 수 있게
public partial class Program;
