using AgentMessageBoard.Server.Mcp;
using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace AgentMessageBoard.Server.Auth;

internal static class AuthenticationExtensions
{
    /// <summary>
    /// Bearer-token authentication against Keycloak (Keycloak.AuthServices validates issuer, signature, lifetime and audience),
    /// plus the MCP authentication handler that answers 401s with a <c>WWW-Authenticate</c> header pointing at
    /// <c>/.well-known/oauth-protected-resource</c> so MCP clients (Claude Code, Codex, ...) can discover Keycloak and log in.
    /// Any authenticated user is authorized; there are no roles.
    /// </summary>
    public static IServiceCollection AddMessageBoardAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MessageBoardOptions>().BindConfiguration(MessageBoardOptions.Section);

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        });

        // AddKeycloakWebApi returns its own builder type, so the MCP challenge handler is added on the same AuthenticationBuilder.
        authentication.AddKeycloakWebApi(configuration, (JwtBearerOptions jwt, IServiceProvider provider) =>
        {
            // Keycloak derives the token issuer from the host the client used. When this server reaches Keycloak through a different
            // host (container network, host.docker.internal, ...), the discovered issuer differs from the one agents' tokens carry, so
            // the public realm URL is accepted as well. JwtBearer appends the discovered issuer to ValidIssuers at validation time.
            var board = provider.GetRequiredService<IOptions<MessageBoardOptions>>().Value;
            if (board.NormalizedAuthorizationServerUrl is { } publicIssuer)
            {
                jwt.TokenValidationParameters.ValidIssuers = [publicIssuer];
            }
        });
        authentication.AddMcp();

        // Keycloak.AuthServices binds its options as named options keyed by the scheme. Fail at startup, not on the first
        // request, when the section is missing.
        services.AddOptions<KeycloakAuthenticationOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(
                keycloak => !string.IsNullOrWhiteSpace(keycloak.AuthServerUrl) && !string.IsNullOrWhiteSpace(keycloak.Realm),
                $"'{KeycloakAuthenticationOptions.Section}:AuthServerUrl' and '{KeycloakAuthenticationOptions.Section}:Realm' must be configured.")
            .ValidateOnStart();

        // The resource metadata is assembled from both option sets when the MCP handler first needs it, so configuration
        // and test-time overrides of either are honoured.
        services.AddOptions<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<MessageBoardOptions>, IOptionsMonitor<KeycloakAuthenticationOptions>>((mcp, board, keycloak) =>
                mcp.ResourceMetadata = CreateResourceMetadata(board.Value, keycloak.Get(JwtBearerDefaults.AuthenticationScheme)));

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AuthenticatedAgent, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    private static ProtectedResourceMetadata CreateResourceMetadata(MessageBoardOptions board, KeycloakAuthenticationOptions keycloak) => new()
    {
        // Without a configured public URL the handler fills in the resource from each request's host and path.
        Resource = string.IsNullOrEmpty(board.PublicUrl) ? null! : $"{board.PublicUrl.TrimEnd('/')}{McpServerExtensions.RoutePattern}",
        AuthorizationServers = { board.NormalizedAuthorizationServerUrl ?? keycloak.KeycloakUrlRealm.TrimEnd('/') },
        ScopesSupported = [.. board.EffectiveScopes],
        BearerMethodsSupported = ["header"],
        ResourceName = McpServerExtensions.ServerTitle,
    };
}
