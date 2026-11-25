using System.Net;
using System.Net.Http.Json;
using AgentMessageBoard.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace AgentMessageBoard.Tests;

public sealed class AuthenticationTests(MessageBoardFactory factory) : IClassFixture<MessageBoardFactory>
{
    private static readonly string McpChallenge =
        """Bearer resource_metadata="http://localhost/.well-known/oauth-protected-resource/mcp" """.TrimEnd();

    private static readonly string ApiChallenge =
        """Bearer resource_metadata="http://localhost/.well-known/oauth-protected-resource/api/messages" """.TrimEnd();

    [Fact]
    public async Task Health_is_anonymous_and_reports_ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", Cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(Cancellation.Token);
        Assert.Equal("ok", body?["status"]);
    }

    [Fact]
    public async Task Mcp_without_token_is_401_with_resource_metadata_challenge()
    {
        using var client = factory.CreateClient();
        using var request = McpToolsListRequest();

        using var response = await client.SendAsync(request, Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(McpChallenge, Assert.Single(response.Headers.GetValues("WWW-Authenticate")));
    }

    [Fact]
    public async Task Api_messages_without_token_is_401_with_resource_metadata_challenge()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiChallenge, Assert.Single(response.Headers.GetValues("WWW-Authenticate")));
    }

    [Fact]
    public async Task Api_messages_with_garbage_token_is_401()
    {
        using var client = factory.CreateClient("not-a-jwt");

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    public async Task Api_messages_rejects_invalid_tokens(string tokenKind)
    {
        using var client = factory.CreateClient(TestTokens.Mint(tokenKind));

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"), "A 401 must carry a WWW-Authenticate challenge.");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    public async Task Mcp_rejects_invalid_tokens(string tokenKind)
    {
        using var client = factory.CreateClient(TestTokens.Mint(tokenKind));
        using var request = McpToolsListRequest();

        using var response = await client.SendAsync(request, Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_messages_with_valid_token_is_200()
    {
        using var client = factory.CreateClient(TestTokens.Valid());

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Jwt_bearer_enforces_issuer_and_the_agent_message_board_audience()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters;

        Assert.True(parameters.ValidateAudience, "Keycloak.AuthServices must leave audience validation on (VerifyTokenAudience=true).");
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey || parameters.IssuerSigningKey is not null);
        Assert.Equal(TestTokens.Audience, options.Audience);
        Assert.Equal(TestTokens.Audience, parameters.ValidAudience);
        Assert.Equal("preferred_username", parameters.NameClaimType);
        Assert.False(options.RequireHttpsMetadata, "SslRequired=none must allow the http://localhost Keycloak.");
        Assert.StartsWith(TestTokens.Issuer, options.Authority);
    }

    [Fact]
    public void Default_schemes_authenticate_with_bearer_and_challenge_with_mcp()
    {
        var options = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultAuthenticateScheme);
        Assert.Equal(McpAuthenticationDefaults.AuthenticationScheme, options.DefaultChallengeScheme);
    }

    /// <summary>A raw Streamable HTTP <c>tools/list</c> request, bypassing the MCP client so the HTTP status and headers are observable.</summary>
    private static HttpRequestMessage McpToolsListRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        return request;
    }
}
