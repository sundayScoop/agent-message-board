namespace AgentMessageBoard.Server.Auth;

/// <summary>How the board advertises itself to OAuth clients (RFC 9728 protected resource metadata). Bound from the <c>MessageBoard</c> section.</summary>
internal sealed class MessageBoardOptions
{
    public const string Section = "MessageBoard";

    /// <summary>
    /// <c>offline_access</c> is included because coding agents run for days: it makes Keycloak issue an offline refresh token that
    /// outlives the SSO session. Claude Code adds it on its own whenever the authorization server advertises it, so the realm has to
    /// accept it either way; <c>keycloak/realm.json</c> assigns it to both clients as an optional scope.
    /// </summary>
    public static readonly string[] DefaultScopes = ["openid", "profile", "email", "offline_access"];

    /// <summary>
    /// Public base URL of this server as clients reach it (e.g. <c>https://board.example.com</c>).
    /// When empty the resource identifier is derived from each incoming request.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// The Keycloak realm URL as agents and browsers reach it (e.g. <c>http://localhost:8080/realms/agents</c>) when it differs
    /// from the URL this server uses (<c>Keycloak:AuthServerUrl</c>), which happens when the server runs in a container or on
    /// another network. It is advertised to clients in the protected resource metadata and accepted as token issuer in
    /// addition to the issuer discovered through <c>Keycloak:AuthServerUrl</c>. Empty means they are the same URL.
    /// </summary>
    public string? AuthorizationServerUrl { get; set; }

    /// <summary><see cref="AuthorizationServerUrl"/> trimmed of whitespace and trailing slashes, or null when not configured.</summary>
    public string? NormalizedAuthorizationServerUrl =>
        string.IsNullOrWhiteSpace(AuthorizationServerUrl) ? null : AuthorizationServerUrl.Trim().TrimEnd('/');

    /// <summary>
    /// Scopes advertised in the metadata. Any authenticated user is authorized; scopes only shape the login request.
    /// Left empty here on purpose: the configuration binder appends to pre-populated arrays, so the defaults live in <see cref="DefaultScopes"/>.
    /// </summary>
    public string[] Scopes { get; set; } = [];

    public IReadOnlyList<string> EffectiveScopes => Scopes.Length > 0 ? Scopes.Distinct(StringComparer.Ordinal).ToArray() : DefaultScopes;
}
