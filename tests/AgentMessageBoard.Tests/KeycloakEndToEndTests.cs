using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentMessageBoard.Tests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AgentMessageBoard.Tests;

/// <summary>
/// The only tests that use a Keycloak-issued token and real OIDC discovery, and the only ones that exercise the realm export
/// itself (client scopes, PKCE login, dynamic registration). They skip (not fail) when Docker is unavailable;
/// run only these with <c>dotnet test --filter-trait "Category=E2E"</c>, or exclude them with <c>--filter-not-trait</c>.
/// </summary>
[Trait("Category", "E2E")]
public sealed class KeycloakEndToEndTests(KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>
{
    private MessageBoardFactory CreateFactory() => new()
    {
        AuthServerUrl = keycloak.BaseAddress,
        UseStaticOidcConfiguration = false, // real OIDC discovery through Keycloak.AuthServices
    };

    [Fact]
    public async Task Alice_posts_over_mcp_with_a_real_keycloak_token_and_reads_it_back_over_rest()
    {
        keycloak.SkipUnlessRunning();
        var ct = Cancellation.Token;

        var token = await keycloak.PasswordGrantAsync("alice", "alice", ct);
        await using var factory = CreateFactory();

        await using var mcp = await McpTestClient.ConnectAsync(factory, token, "e2e-agent", "0.1.0", ct);
        var posted = await mcp.PostAsync("fix", "End-to-end: Keycloak-issued token accepted", ct);

        Assert.Equal("alice", posted.PostedBy);
        Assert.Equal("e2e-agent/0.1.0", posted.UserAgent);

        using var http = factory.CreateClient(token);
        var messages = await http.GetFromJsonAsync<List<MessageRecord>>("/api/messages", MessageJson.Options, ct);

        Assert.NotNull(messages);
        var only = Assert.Single(messages);
        Assert.Equal(posted, only);
        Assert.Equal("alice", only.PostedBy);
    }

    [Fact]
    public async Task Self_minted_tokens_are_rejected_when_keys_come_from_real_discovery()
    {
        keycloak.SkipUnlessRunning();
        var ct = Cancellation.Token;

        await using var factory = CreateFactory();
        using var forged = factory.CreateClient(TestTokens.Create(issuer: $"{keycloak.BaseAddress}realms/{KeycloakFixture.Realm}"));
        using var genuine = factory.CreateClient(await keycloak.PasswordGrantAsync("alice", "alice", ct));

        using var forgedResponse = await forged.GetAsync("/api/messages", ct);
        using var genuineResponse = await genuine.GetAsync("/api/messages", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, genuineResponse.StatusCode);
    }

    [Fact]
    public async Task Protected_resource_metadata_points_at_the_running_keycloak_realm()
    {
        keycloak.SkipUnlessRunning();
        var ct = Cancellation.Token;

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-protected-resource/mcp", ct));
        var servers = document.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal([$"{keycloak.BaseAddress}realms/{KeycloakFixture.Realm}"], servers);
    }

    [Fact]
    public async Task The_pre_registered_agent_client_logs_in_with_the_scopes_claude_code_requests()
    {
        keycloak.SkipUnlessRunning();
        var ct = Cancellation.Token;
        await using var factory = CreateFactory();

        // Claude Code asks for the metadata's scopes_supported and appends offline_access whenever Keycloak advertises it,
        // which Keycloak always does. A realm that does not let the client request it answers with invalid_scope.
        var scope = await ScopesAnAgentRequestsAsync(factory, ct);
        var tokens = await keycloak.LoginWithAuthorizationCodeAsync(KeycloakFixture.AgentClientId, "http://localhost:8765/callback", scope, "alice", "alice", ct);

        Assert.Contains("offline_access", tokens.Scope.Split(' '));
        Assert.Equal("Offline", new JsonWebToken(tokens.RefreshToken).GetPayloadValue<string>("typ"));
        Assert.Contains("agent-message-board", new JsonWebToken(tokens.AccessToken).Audiences);

        using var http = factory.CreateClient(tokens.AccessToken);
        using var response = await http.GetAsync("/api/messages", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_dynamically_registered_client_may_request_the_same_scopes()
    {
        keycloak.SkipUnlessRunning();
        var ct = Cancellation.Token;
        await using var factory = CreateFactory();

        const string callback = "http://localhost:50123/callback";
        var clientId = await keycloak.RegisterClientAsync(callback, ct);

        var (status, location) = await keycloak.AuthorizeAsync(clientId, callback, await ScopesAnAgentRequestsAsync(factory, ct), ct);

        Assert.True(status == HttpStatusCode.OK, $"Expected Keycloak's login page, got {(int)status} redirecting to {location}");
    }

    /// <summary>The server's advertised <c>scopes_supported</c> plus <c>offline_access</c>, as Claude Code sends them.</summary>
    private static async Task<string> ScopesAnAgentRequestsAsync(MessageBoardFactory factory, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-protected-resource/mcp", ct));
        var advertised = document.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()!);

        return string.Join(' ', advertised.Append("offline_access").Distinct(StringComparer.Ordinal));
    }
}
