using AgentMessageBoard.Server.Data;
using AgentMessageBoard.Server.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>
/// Hosts the server in-memory with its own throw-away SQLite file (deleted on dispose) and, by default,
/// a static OIDC configuration so the JWT bearer handler trusts <see cref="TestTokens.SigningKey"/> without
/// contacting Keycloak. Set <see cref="UseStaticOidcConfiguration"/> to false to exercise real discovery.
/// </summary>
public sealed class MessageBoardFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), "agent-message-board-tests", $"{Guid.NewGuid():N}.db");

    /// <summary>Overrides <c>MessageBoard:PublicUrl</c> when set.</summary>
    public string? PublicUrl { get; init; }

    /// <summary>Overrides <c>Keycloak:AuthServerUrl</c>. The default matches appsettings.json.</summary>
    public string AuthServerUrl { get; init; } = "http://localhost:8180/";

    /// <summary>When true (default) no OIDC metadata is fetched; the issuer and signing key are fixed to the test key.</summary>
    public bool UseStaticOidcConfiguration { get; init; } = true;

    /// <summary>Extra configuration keys applied with <c>UseSetting</c>.</summary>
    public IDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        builder.UseEnvironment("Testing");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting($"ConnectionStrings:{PersistenceExtensions.ConnectionStringName}", $"Data Source={DatabasePath}");
        builder.UseSetting("Keycloak:AuthServerUrl", AuthServerUrl);

        if (PublicUrl is not null)
        {
            builder.UseSetting("MessageBoard:PublicUrl", PublicUrl);
        }

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        if (UseStaticOidcConfiguration)
        {
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                    configuration.SigningKeys.Add(TestTokens.SigningKey);

                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.TokenValidationParameters.ValidIssuer = configuration.Issuer;
                    options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                }));
        }
    }

    /// <summary>An HttpClient carrying <c>Authorization: Bearer {token}</c>.</summary>
    public HttpClient CreateClient(string bearerToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearerToken);
        return client;
    }

    /// <summary>Inserts rows directly, bypassing validation, so tests control timestamps and ordering.</summary>
    public async Task<IReadOnlyList<Message>> SeedAsync(params Message[] messages)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessageBoardDbContext>();
        db.Messages.AddRange(messages);
        await db.SaveChangesAsync(Cancellation.Token);
        return messages;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // Microsoft.Data.Sqlite pools connections; closed pooled handles still hold the file open.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                TryDelete(DatabasePath + suffix);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a stale temp file is not worth failing a test run over.
        }
    }
}
