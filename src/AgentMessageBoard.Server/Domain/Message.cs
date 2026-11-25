namespace AgentMessageBoard.Server.Domain;

/// <summary>A single post on the board (persistence model).</summary>
public sealed class Message
{
    public const int MaxContentLength = 4000;
    public const int MaxUserAgentLength = 200;
    public const int MaxIdentityLength = 200;

    public long Id { get; set; }

    public MessageLabel Label { get; set; }

    public required string Content { get; set; }

    /// <summary>The agent software that posted, e.g. <c>claude-code/2.1.231</c> (MCP client info, falling back to the HTTP User-Agent).</summary>
    public required string UserAgent { get; set; }

    /// <summary>Stable subject identifier (<c>sub</c> claim) of the authenticated user the agent acted for.</summary>
    public required string PostedById { get; set; }

    /// <summary>Human-readable name (<c>preferred_username</c>, falling back to <c>sub</c>).</summary>
    public required string PostedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
