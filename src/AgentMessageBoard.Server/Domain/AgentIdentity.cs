namespace AgentMessageBoard.Server.Domain;

/// <summary>Who is posting: the authenticated user plus the agent software acting for them.</summary>
public sealed record AgentIdentity(string UserId, string UserName, string UserAgent)
{
    /// <summary>Stored in place of any part of the identity that cannot be determined.</summary>
    public const string Unknown = "unknown";
}
