using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>
/// Mints RS256 JWTs shaped like Keycloak access tokens. <see cref="SigningKey"/> is the only key the test host trusts,
/// so tokens signed with anything else must be rejected.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "http://localhost:8180/realms/agents";
    public const string Audience = "agent-message-board";
    public const string ClientId = "agent-board-e2e";

    /// <summary>One signing key per test run (the only shared static state in the suite).</summary>
    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "test-signing-key" };

    public static string Valid(string username = "alice") => Create(username);

    public static string Expired(string username = "alice") => Create(username, expires: DateTime.UtcNow.AddMinutes(-10));

    public static string WrongAudience(string username = "alice") => Create(username, audience: "some-other-api");

    public static string WrongIssuer(string username = "alice") => Create(username, issuer: "http://localhost:8180/realms/other");

    public static string WrongKey(string username = "alice") =>
        Create(username, signingKey: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rogue-key" });

    /// <summary>Mints the named variation; used by theories so the data stays serializable.</summary>
    public static string Mint(string kind, string username = "alice") => kind switch
    {
        "valid" => Valid(username),
        "expired" => Expired(username),
        "wrong-audience" => WrongAudience(username),
        "wrong-issuer" => WrongIssuer(username),
        "wrong-key" => WrongKey(username),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
    };

    public static string SubjectFor(string username) => $"{username}-subject-id";

    public static string Create(
        string username = "alice",
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddMinutes(5);
        var issuedAt = exp < now ? exp.AddMinutes(-10) : now.AddSeconds(-5);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = exp,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = SubjectFor(username),
                ["preferred_username"] = username,
                ["email"] = $"{username}@example.test",
                ["azp"] = ClientId,
                ["typ"] = "Bearer",
            },
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
