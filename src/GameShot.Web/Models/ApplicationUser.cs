using Microsoft.AspNetCore.Identity;

namespace GameShot.Web.Models;

public class ApplicationUser : IdentityUser
{
    /// <summary>화면에 보여줄 닉네임</summary>
    public string? Nickname { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
