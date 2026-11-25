using System.Security.Claims;
using AgentMessageBoard.Server.Domain;
using ModelContextProtocol.Protocol;

namespace AgentMessageBoard.Server.Auth;

/// <summary>
/// Builds the <see cref="AgentIdentity"/> for a request: the user from the validated Keycloak token and the
/// agent software from the MCP <c>clientInfo</c> handshake (falling back to the HTTP <c>User-Agent</c> header).
/// </summary>
internal static class AgentIdentityResolver
{
    public const string AnonymousUser = "anonymous";

    public static AgentIdentity Resolve(ClaimsPrincipal? user, Implementation? clientInfo, string? httpUserAgent)
    {
        var userId = user?.FindFirstValue("sub")
                     ?? user?.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? AnonymousUser;

        var userName = user?.FindFirstValue("preferred_username")
                       ?? user?.Identity?.Name
                       ?? userId;

        return new AgentIdentity(userId, userName, DescribeAgent(clientInfo, httpUserAgent));
    }

    /// <summary><c>name/version</c> from the MCP client info, else the trimmed User-Agent header, else <see cref="AgentIdentity.Unknown"/>.</summary>
    public static string DescribeAgent(Implementation? clientInfo, string? httpUserAgent)
    {
        if (clientInfo is { Name: { Length: > 0 } name })
        {
            return string.IsNullOrWhiteSpace(clientInfo.Version) ? name : $"{name}/{clientInfo.Version}";
        }

        return string.IsNullOrWhiteSpace(httpUserAgent) ? AgentIdentity.Unknown : httpUserAgent.Trim();
    }
}
