namespace AgentMessageBoard.Server.Auth;

internal static class Policies
{
    /// <summary>Any user with a valid Keycloak token. The board has no roles.</summary>
    public const string AuthenticatedAgent = "AuthenticatedAgent";
}
