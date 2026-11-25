using System.Net;
using AgentMessageBoard.Tests.Infrastructure;

namespace AgentMessageBoard.Tests;

/// <summary>
/// <c>MessageBoard:AuthorizationServerUrl</c> covers the containerized server: Keycloak is reached through one host by the server
/// and through another by the agents, so tokens carry an issuer that differs from the discovered one.
/// </summary>
public sealed class AuthorizationServerUrlTests
{
    private const string PublicIssuer = "http://localhost:8080/realms/agents";

    [Fact]
    public async Task Tokens_issued_under_the_public_realm_url_are_accepted_when_it_is_configured()
    {
        await using var factory = new MessageBoardFactory { Settings = { ["MessageBoard:AuthorizationServerUrl"] = PublicIssuer + "/" } };
        using var client = factory.CreateClient(TestTokens.Create(issuer: PublicIssuer));

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_issued_under_the_discovered_issuer_stay_accepted()
    {
        await using var factory = new MessageBoardFactory { Settings = { ["MessageBoard:AuthorizationServerUrl"] = PublicIssuer } };
        using var client = factory.CreateClient(TestTokens.Valid());

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Other_realms_on_the_public_host_are_still_rejected()
    {
        await using var factory = new MessageBoardFactory { Settings = { ["MessageBoard:AuthorizationServerUrl"] = PublicIssuer } };
        using var client = factory.CreateClient(TestTokens.Create(issuer: "http://localhost:8080/realms/other"));

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_setting_only_the_discovered_issuer_is_trusted()
    {
        await using var factory = new MessageBoardFactory();
        using var client = factory.CreateClient(TestTokens.Create(issuer: PublicIssuer));

        using var response = await client.GetAsync("/api/messages", Cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
