namespace GameShot.Web.Services;

/// <summary>한국 표준시(KST) 변환</summary>
public static class KoreanTime
{
    private static readonly TimeZoneInfo Kst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

    public static DateTime Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Kst).DateTime;

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock));

    /// <summary>DB에 UTC로 저장된 시각을 화면용 한국 시각으로</summary>
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Kst);
}
