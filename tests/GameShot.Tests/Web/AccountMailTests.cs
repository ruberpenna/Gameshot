using System.Net;
using GameShot.Web.Services.Mail;

namespace GameShot.Tests.Web;

/// <summary>비밀번호 찾기·이메일 인증을 메일 링크까지 따라가며 검증</summary>
public sealed class AccountMailTests(GameShotFactory factory) : IClassFixture<GameShotFactory>
{
    private static string NewEmail() => $"mail-{Guid.NewGuid():N}@example.test";
    private static string NewNickname() => "메일" + Random.Shared.Next(100_000, 999_999);

    private static Task<HttpResponseMessage> RequestReset(HttpClient client, string email) =>
        client.SubmitFormAsync("/Identity/Account/ForgotPassword", new Dictionary<string, string> { ["Email"] = email });

    [Fact]
    public async Task 비밀번호_찾기_메일의_링크로_새_비밀번호를_정할_수_있다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());
        await client.LogoutAsync();

        var requested = await RequestReset(client, email);
        Assert.EndsWith("/ForgotPasswordConfirmation", requested.RequestMessage!.RequestUri!.AbsolutePath);

        var mail = await factory.LastMailToAsync(email, MailKinds.PasswordReset);
        Assert.NotNull(mail);
        var link = GameShotFactory.LinkIn(mail);
        Assert.StartsWith("/Identity/Account/ResetPassword?", link);

        var reset = await client.SubmitFormAsync(link, new Dictionary<string, string>
        {
            ["Input.UserId"] = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + link).Query)["userId"]!,
            ["Input.Code"] = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + link).Query)["code"]!,
            ["Input.Password"] = "resetpass789",
            ["Input.ConfirmPassword"] = "resetpass789",
        }, "/Identity/Account/ResetPassword");
        Assert.EndsWith("/ResetPasswordConfirmation", reset.RequestMessage!.RequestUri!.AbsolutePath);

        Assert.Contains("이메일 또는 비밀번호가 맞지 않습니다.", await (await client.LoginAsync(email, "gameshot123")).TextAsync());
        Assert.Equal("/", (await client.LoginAsync(email, "resetpass789")).RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task 재설정_링크는_한_번만_쓸_수_있고_조작하면_거부된다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());
        await client.LogoutAsync();
        await RequestReset(client, email);
        var link = GameShotFactory.LinkIn((await factory.LastMailToAsync(email, MailKinds.PasswordReset))!);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + link).Query);

        Dictionary<string, string> Form(string code) => new()
        {
            ["Input.UserId"] = query["userId"]!,
            ["Input.Code"] = code,
            ["Input.Password"] = "resetpass789",
            ["Input.ConfirmPassword"] = "resetpass789",
        };

        var forged = await (await client.SubmitFormAsync(link, Form(query["code"]!.Replace('A', 'B').Replace('a', 'b')), "/Identity/Account/ResetPassword")).TextAsync();
        Assert.Contains("재설정 링크가 올바르지 않거나 만료되었습니다", forged);

        await client.SubmitFormAsync(link, Form(query["code"]!), "/Identity/Account/ResetPassword");
        // 비밀번호가 바뀌면 보안 스탬프가 바뀌어 같은 링크를 다시 쓸 수 없다
        var reused = await (await client.SubmitFormAsync(link, Form(query["code"]!), "/Identity/Account/ResetPassword")).TextAsync();
        Assert.Contains("재설정 링크가 올바르지 않거나 만료되었습니다", reused);
    }

    [Fact]
    public async Task 가입하지_않은_이메일도_같은_화면이고_메일은_가지_않는다()
    {
        var email = NewEmail();

        var response = await RequestReset(factory.CreateClient(), email);

        Assert.EndsWith("/ForgotPasswordConfirmation", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(0, await factory.MailCountAsync(email));
    }

    [Fact]
    public async Task 재설정_메일을_연달아_요청해도_한_통만_간다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());
        await client.LogoutAsync();

        await RequestReset(client, email);
        await RequestReset(client, email);
        await RequestReset(client, email);

        // 가입 인증 메일 1통 + 재설정 메일 1통
        Assert.Equal(2, await factory.MailCountAsync(email));
    }

    [Fact]
    public async Task 가입하면_인증_메일이_가고_인증_전에는_주문서에_들어갈_수_없다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());

        var mail = await factory.LastMailToAsync(email, MailKinds.EmailConfirm);
        Assert.NotNull(mail);

        var checkout = await client.GetAsync("/checkout");
        Assert.Equal("/Identity/Account/Manage", checkout.RequestMessage!.RequestUri!.AbsolutePath);
        var profile = await checkout.TextAsync();
        Assert.Contains("이메일 인증을 마쳐야 주문할 수 있습니다", profile);
        Assert.Contains("미인증", profile);

        var confirmed = await (await client.GetAsync(GameShotFactory.LinkIn(mail))).TextAsync();
        Assert.Contains("이메일 인증을 마쳤습니다", confirmed);

        // 장바구니가 비어 있으니 이제는 주문서 대신 장바구니로 간다 (인증 확인은 통과)
        var afterConfirm = await client.GetAsync("/checkout");
        Assert.Equal("/cart", afterConfirm.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("인증됨", await client.GetStringAsync("/Identity/Account/Manage"));
    }

    [Fact]
    public async Task 인증_메일은_다시_받을_수_있지만_연타는_막는다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());

        var again = await (await client.SubmitFormAsync("/Identity/Account/Manage", new Dictionary<string, string>(),
            "/Identity/Account/Manage?handler=SendConfirmation")).TextAsync();

        Assert.Contains("방금 인증 메일을 보냈습니다", again);
        Assert.Equal(1, await factory.MailCountAsync(email));
    }

    [Fact]
    public async Task 잘못된_인증_링크는_실패_안내를_보여준다()
    {
        var html = await factory.CreateClient().GetStringAsync("/Identity/Account/ConfirmEmail?userId=nobody&code=bm9wZQ");

        Assert.Contains("인증 링크가 올바르지 않거나 만료되었습니다", html);
    }

    [Fact]
    public async Task 로그인_화면에서_비밀번호_찾기로_갈_수_있다()
    {
        var login = await factory.CreateClient().GetStringAsync("/Identity/Account/Login");
        var forgot = await factory.CreateClient().GetAsync("/Identity/Account/ForgotPassword");

        Assert.Contains("/Identity/Account/ForgotPassword", login);
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
    }
}
