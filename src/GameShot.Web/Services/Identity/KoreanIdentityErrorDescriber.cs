using Microsoft.AspNetCore.Identity;

namespace GameShot.Web.Services.Identity;

/// <summary>ASP.NET Identity 오류 메시지 한국어판</summary>
public class KoreanIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "알 수 없는 오류가 발생했습니다.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "다른 곳에서 정보가 바뀌었습니다. 새로고침 후 다시 시도하세요.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "현재 비밀번호가 맞지 않습니다.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "유효하지 않거나 만료된 요청입니다.");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "이미 연결된 계정입니다.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), $"'{userName}'은(는) 쓸 수 없는 아이디입니다.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), $"'{email}'은(는) 올바른 이메일 주소가 아닙니다.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"'{userName}'은(는) 이미 가입된 이메일입니다.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), $"'{email}'은(는) 이미 가입된 이메일입니다.");
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), $"'{role}'은(는) 쓸 수 없는 역할 이름입니다.");
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), $"'{role}' 역할이 이미 있습니다.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "이미 비밀번호가 설정되어 있습니다.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "이 계정은 잠금 기능을 쓰지 않습니다.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), $"이미 '{role}' 역할이 있습니다.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), $"'{role}' 역할이 없습니다.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"비밀번호는 {length}자 이상이어야 합니다.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"비밀번호에 서로 다른 문자가 {uniqueChars}개 이상 있어야 합니다.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "비밀번호에 특수문자가 하나 이상 있어야 합니다.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "비밀번호에 숫자가 하나 이상 있어야 합니다.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "비밀번호에 영문 소문자가 하나 이상 있어야 합니다.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "비밀번호에 영문 대문자가 하나 이상 있어야 합니다.");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "복구 코드가 맞지 않습니다.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
