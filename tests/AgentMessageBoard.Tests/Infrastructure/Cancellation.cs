namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>Shorthand for the xunit v3 per-test cancellation token.</summary>
internal static class Cancellation
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
