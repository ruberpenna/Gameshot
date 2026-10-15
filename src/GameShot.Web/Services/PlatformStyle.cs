namespace GameShot.Web.Services;

/// <summary>플랫폼 배지 색 (화면용). 플랫폼 이름으로 계열을 고른다.</summary>
public static class PlatformStyle
{
    public static string CssClass(string platformName) => platformName switch
    {
        _ when platformName.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) => "pf-ps",
        _ when platformName.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) => "pf-nin",
        _ when platformName.Contains("Xbox", StringComparison.OrdinalIgnoreCase) => "pf-xbox",
        _ => "pf-etc",
    };

    /// <summary>카드에 쓸 짧은 이름 (PlayStation 5 → PS5)</summary>
    public static string ShortName(string platformName) => platformName
        .Replace("PlayStation ", "PS", StringComparison.OrdinalIgnoreCase)
        .Replace("Nintendo ", "", StringComparison.OrdinalIgnoreCase)
        .Replace("Xbox Series X|S", "Xbox", StringComparison.OrdinalIgnoreCase);
}
