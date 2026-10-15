using Microsoft.AspNetCore.Identity;

namespace GameShot.Web.Models;

public class ApplicationUser : IdentityUser
{
    /// <summary>화면에 보여줄 닉네임</summary>
    public string? Nickname { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 포인트 잔액 (1포인트 = 1원). 사용할 때 "잔액 >= 사용액" 조건부 UPDATE로 줄여서 동시에 써도 음수가 되지 않게 한다.
    /// </summary>
    public int PointBalance { get; set; }
}
