namespace GameShot.Tests;

/// <summary>테스트에서 시간을 직접 움직이는 TimeProvider</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public TestClock() : this(new DateTimeOffset(2026, 10, 15, 1, 0, 0, TimeSpan.Zero)) { }

    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
