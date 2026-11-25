using System.Net;
using System.Text.Json;
using AgentMessageBoard.Tests.Infrastructure;

namespace AgentMessageBoard.Tests;

/// <summary>RFC 9728 metadata served by the MCP authentication handler. Each test configures its own host.</summary>
public sealed class ProtectedResourceMetadataTests
{
    private const string MetadataPath = "/.well-known/oauth-protected-resource";

    [Fact]
    public async Task Metadata_for_mcp_reflects_the_configured_public_url_and_keycloak_realm()
    {
        await using var factory = new MessageBoardFactory { PublicUrl = "https://board.example.test/" };

        using var metadata = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");
        var root = metadata.RootElement;

        Assert.Equal("https://board.example.test/mcp", root.GetProperty("resource").GetString());
        Assert.Equal(["http://localhost:8180/realms/agents"], Strings(root, "authorization_servers"));
        Assert.Equal(["header"], Strings(root, "bearer_methods_supported"));
        Assert.Equal(["openid", "profile", "email", "offline_access"], Strings(root, "scopes_supported"));
        Assert.Equal("Agent Message Board", root.GetProperty("resource_name").GetString());
    }

    [Fact]
    public async Task Metadata_uses_the_default_public_url_from_appsettings()
    {
        await using var factory = new MessageBoardFactory();

        using var metadata = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");

        Assert.Equal("http://localhost:5080/mcp", metadata.RootElement.GetProperty("resource").GetString());
    }

    [Fact]
    public async Task Metadata_is_also_served_at_the_root_well_known_path()
    {
        await using var factory = new MessageBoardFactory();

        using var metadata = await GetMetadataAsync(factory, MetadataPath);

        Assert.Equal("http://localhost:5080/mcp", metadata.RootElement.GetProperty("resource").GetString());
        Assert.Equal(["http://localhost:8180/realms/agents"], Strings(metadata.RootElement, "authorization_servers"));
    }

    [Fact]
    public async Task Resource_is_derived_from_the_request_when_no_public_url_is_configured()
    {
        await using var factory = new MessageBoardFactory { Settings = { ["MessageBoard:PublicUrl"] = "" } };

        using var forMcp = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");
        using var forApi = await GetMetadataAsync(factory, $"{MetadataPath}/api/messages");

        Assert.Equal("http://localhost/mcp", forMcp.RootElement.GetProperty("resource").GetString());
        Assert.Equal("http://localhost/api/messages", forApi.RootElement.GetProperty("resource").GetString());
        Assert.Equal(["http://localhost:8180/realms/agents"], Strings(forMcp.RootElement, "authorization_servers"));
    }

    [Fact]
    public async Task Authorization_server_follows_the_keycloak_configuration()
    {
        await using var factory = new MessageBoardFactory
        {
            AuthServerUrl = "https://sso.example.test/auth",
            Settings = { ["Keycloak:Realm"] = "other-realm" },
        };

        using var metadata = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");

        Assert.Equal(["https://sso.example.test/auth/realms/other-realm"], Strings(metadata.RootElement, "authorization_servers"));
    }

    [Fact]
    public async Task Authorization_server_can_be_overridden_when_clients_reach_keycloak_through_a_different_host()
    {
        // The server (in a container) talks to Keycloak via host.docker.internal; agents on the host use localhost.
        await using var factory = new MessageBoardFactory
        {
            AuthServerUrl = "http://host.docker.internal:8080/",
            Settings = { ["MessageBoard:AuthorizationServerUrl"] = "http://localhost:8080/realms/agents/" },
        };

        using var metadata = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");

        Assert.Equal(["http://localhost:8080/realms/agents"], Strings(metadata.RootElement, "authorization_servers"));
    }

    [Fact]
    public async Task Scopes_supported_has_no_duplicates_even_if_configuration_repeats_them()
    {
        // The configuration binder appends array entries; a stray duplicate must not leak into the advertised scopes.
        await using var factory = new MessageBoardFactory { Settings = { ["MessageBoard:Scopes:4"] = "openid", ["MessageBoard:Scopes:5"] = "email" } };

        using var metadata = await GetMetadataAsync(factory, $"{MetadataPath}/mcp");

        var scopes = Strings(metadata.RootElement, "scopes_supported");
        Assert.Equal(scopes.Distinct(StringComparer.Ordinal), scopes);
        Assert.Equal(["openid", "profile", "email", "offline_access"], scopes);
    }

    /// <summary>Anonymous GET of a metadata document; asserts the 200 and the JSON content type on the way.</summary>
    private static async Task<JsonDocument> GetMetadataAsync(MessageBoardFactory factory, string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, Cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
    }

    private static string[] Strings(JsonElement root, string property) =>
        [.. root.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];
}
