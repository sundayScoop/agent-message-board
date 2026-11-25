namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>A clock the test controls. <see cref="GetUtcNow"/> only moves when the test says so.</summary>
internal sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void SetUtcNow(DateTimeOffset now) => _now = now;
}
