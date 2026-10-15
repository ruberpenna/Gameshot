using System.Net;

namespace GameShot.Tests.Web;

/// <summary>회원가입·로그인·내 정보 화면을 실제 HTTP 요청으로 검증</summary>
public sealed class AccountPagesTests(GameShotFactory factory) : IClassFixture<GameShotFactory>
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";
    private static string NewNickname() => "닉" + Random.Shared.Next(100_000, 999_999);

    [Fact]
    public async Task 회원가입_로그인_화면은_한국어다()
    {
        var client = factory.CreateClient();

        var register = await client.GetStringAsync("/Identity/Account/Register");
        var login = await client.GetStringAsync("/Identity/Account/Login");

        Assert.Contains("<title>회원가입 - GameShot</title>", register);
        Assert.Contains("닉네임", register);
        Assert.Contains("약관", register);
        Assert.Contains("<title>로그인 - GameShot</title>", login);
        Assert.DoesNotContain("Register", login.Replace("/Identity/Account/Register", ""));
    }

    [Fact]
    public async Task 가입하면_바로_로그인되고_상단에_닉네임이_보인다()
    {
        var client = factory.CreateClient();
        var nickname = NewNickname();

        var response = await client.RegisterAsync(NewEmail(), nickname);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/", response.RequestMessage!.RequestUri!.AbsolutePath);
        var home = await response.TextAsync();
        Assert.Contains($"{nickname} 님", home);
        Assert.Contains($"{nickname}님, 가입을 환영합니다!", home);
    }

    [Fact]
    public async Task 같은_닉네임이나_이메일로는_가입할_수_없다()
    {
        var email = NewEmail();
        var nickname = NewNickname();
        await factory.CreateClient().RegisterAsync(email, nickname);

        var sameNickname = await (await factory.CreateClient().RegisterAsync(NewEmail(), nickname)).TextAsync();
        var sameEmail = await (await factory.CreateClient().RegisterAsync(email, NewNickname())).TextAsync();

        Assert.Contains("이미 쓰고 있는 닉네임입니다.", sameNickname);
        Assert.Contains("이미 가입된 이메일입니다.", sameEmail);
    }

    [Theory]
    [InlineData("관리자", "쓸 수 없는 닉네임입니다.")]
    [InlineData("a", "한글·영문·숫자 2~12자")]
    [InlineData("공백 있음", "한글·영문·숫자 2~12자")]
    public async Task 닉네임_규칙(string nickname, string expectedMessage)
    {
        var html = await (await factory.CreateClient().RegisterAsync(NewEmail(), nickname)).TextAsync();

        Assert.Contains(expectedMessage, html);
        Assert.Contains("<title>회원가입 - GameShot</title>", html);
    }

    [Fact]
    public async Task 약관에_동의하지_않으면_가입되지_않는다()
    {
        var html = await (await factory.CreateClient().RegisterAsync(NewEmail(), NewNickname(), agree: false)).TextAsync();

        Assert.Contains("약관에 동의해야 가입할 수 있습니다.", html);
    }

    [Theory]
    [InlineData("abcdefgh", "숫자가 하나 이상")]
    [InlineData("12345678", "영문 소문자가 하나 이상")]
    [InlineData("abc123", "8자 이상")]
    public async Task 비밀번호_규칙은_한국어로_알려준다(string password, string expected)
    {
        var html = await (await factory.CreateClient().RegisterAsync(NewEmail(), NewNickname(), password)).TextAsync();

        Assert.Contains(expected, html);
    }

    [Fact]
    public async Task 로그인을_5번_틀리면_잠긴다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());
        await client.LogoutAsync();

        var first = await (await client.LoginAsync(email, "wrong-password1")).TextAsync();
        Assert.Contains("이메일 또는 비밀번호가 맞지 않습니다.", first);
        for (var i = 0; i < 3; i++)
        {
            await client.LoginAsync(email, "wrong-password1");
        }
        var fifth = await client.LoginAsync(email, "wrong-password1");

        Assert.EndsWith("/Identity/Account/Lockout", fifth.RequestMessage!.RequestUri!.AbsolutePath);
        // 잠긴 동안은 맞는 비밀번호로도 못 들어간다
        var correct = await client.LoginAsync(email, "gameshot123");
        Assert.EndsWith("/Identity/Account/Lockout", correct.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task 로그인_후_다른_사이트로는_보내지_않는다()
    {
        var email = NewEmail();
        await factory.CreateClient().RegisterAsync(email, NewNickname());
        var client = factory.CreateNoRedirectClient();

        var evil = await client.LoginAsync(email, "gameshot123", returnUrl: "https://evil.example/steal");

        Assert.Equal(HttpStatusCode.Redirect, evil.StatusCode);
        Assert.Equal("/", evil.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task 로그인하면_원래_가려던_곳으로_돌아간다()
    {
        var email = NewEmail();
        await factory.CreateClient().RegisterAsync(email, NewNickname());
        var client = factory.CreateClient();

        var toCheckout = await client.GetAsync("/my/points");
        Assert.Equal("/Identity/Account/Login", toCheckout.RequestMessage!.RequestUri!.AbsolutePath);

        var login = await client.LoginAsync(email, "gameshot123", returnUrl: "/my/points");
        Assert.Equal("/my/points", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("/Identity/Account/LoginWith2fa")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    [InlineData("/Identity/Account/Manage/Email")]
    [InlineData("/identity/account/manage/twofactorauthentication")]
    public async Task 지원하지_않는_기본_페이지는_막혀_있다(string path)
    {
        var client = factory.CreateClient();
        await client.RegisterAsync(NewEmail(), NewNickname());

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 프로필에서_닉네임을_바꾸면_상단에도_바로_반영된다()
    {
        var taken = NewNickname();
        await factory.CreateClient().RegisterAsync(NewEmail(), taken);
        var client = factory.CreateClient();
        await client.RegisterAsync(NewEmail(), NewNickname());

        var conflict = await (await client.SubmitFormAsync("/Identity/Account/Manage", new Dictionary<string, string>
        {
            ["Input.Nickname"] = taken,
            ["Input.PhoneNumber"] = "",
        })).TextAsync();
        Assert.Contains("이미 쓰고 있는 닉네임입니다.", conflict);

        var renamed = NewNickname();
        var ok = await (await client.SubmitFormAsync("/Identity/Account/Manage", new Dictionary<string, string>
        {
            ["Input.Nickname"] = renamed,
            ["Input.PhoneNumber"] = "01012345678",
        })).TextAsync();
        Assert.Contains("프로필을 저장했습니다.", ok);
        Assert.Contains($"{renamed} 님", ok);
        Assert.Contains("010-1234-5678", ok);
    }

    [Fact]
    public async Task 비밀번호를_바꾸면_새_비밀번호로만_로그인된다()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await client.RegisterAsync(email, NewNickname());

        var wrongOld = await (await client.SubmitFormAsync("/Identity/Account/Manage/ChangePassword", new Dictionary<string, string>
        {
            ["Input.OldPassword"] = "not-mine-1",
            ["Input.NewPassword"] = "newpass456",
            ["Input.ConfirmPassword"] = "newpass456",
        })).TextAsync();
        Assert.Contains("현재 비밀번호가 맞지 않습니다.", wrongOld);

        var changed = await (await client.SubmitFormAsync("/Identity/Account/Manage/ChangePassword", new Dictionary<string, string>
        {
            ["Input.OldPassword"] = "gameshot123",
            ["Input.NewPassword"] = "newpass456",
            ["Input.ConfirmPassword"] = "newpass456",
        })).TextAsync();
        Assert.Contains("비밀번호를 바꿨습니다.", changed);

        await client.LogoutAsync();
        Assert.Contains("이메일 또는 비밀번호가 맞지 않습니다.", await (await client.LoginAsync(email, "gameshot123")).TextAsync());
        Assert.Equal("/", (await client.LoginAsync(email, "newpass456")).RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task 로그인한_상태로_로그인_가입_화면에_가면_돌려보낸다()
    {
        var client = factory.CreateClient();
        await client.RegisterAsync(NewEmail(), NewNickname());

        var login = await client.GetAsync("/Identity/Account/Login?returnUrl=%2Fcart");
        var register = await client.GetAsync("/Identity/Account/Register");

        Assert.Equal("/cart", login.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("/", register.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task 로그아웃은_POST로만_된다()
    {
        var client = factory.CreateClient();
        var nickname = NewNickname();
        await client.RegisterAsync(NewEmail(), nickname);

        // 링크(GET)로는 로그아웃되지 않는다
        var viaGet = await (await client.GetAsync("/Identity/Account/Logout")).TextAsync();
        Assert.Contains($"{nickname} 님", viaGet);

        var viaPost = await (await client.LogoutAsync()).TextAsync();
        Assert.Contains("로그아웃했습니다.", viaPost);
        Assert.DoesNotContain($"{nickname} 님", viaPost);
    }
}
