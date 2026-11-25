using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Testcontainers.Keycloak;

namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>Result of a code exchange: the tokens and the scope Keycloak actually granted.</summary>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string Scope);

/// <summary>
/// Starts a real Keycloak once per class from <c>keycloak/realm.json</c>. Tests skip (not fail) when Docker
/// or the realm export is unavailable. Startup failures with both present are reported as failures.
/// </summary>
public sealed partial class KeycloakFixture : IAsyncLifetime
{
    public const string Image = "quay.io/keycloak/keycloak:26.7.5";
    public const string Realm = "agents";

    /// <summary>Password-grant client for tests and curl.</summary>
    public const string PublicClientId = "agent-board-e2e";

    /// <summary>The pre-registered agent client (authorization code + PKCE); its redirect URIs allow the loopback ports 8765 and 8766.</summary>
    public const string AgentClientId = "agent-board-cli";

    private static readonly string[] RegistrationGrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] RegistrationResponseTypes = ["code"];

    private KeycloakContainer? _container;

    /// <summary>Why the E2E tests cannot run, or null when Keycloak is up.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Dynamically skips the calling test when Keycloak could not be started.</summary>
    public void SkipUnlessRunning() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? "");

    /// <summary>Keycloak base address with a trailing slash, e.g. <c>http://localhost:32768/</c>.</summary>
    public string BaseAddress { get; private set; } = "";

    private string RealmUrl => $"{BaseAddress}realms/{Realm}";

    public async ValueTask InitializeAsync()
    {
        if (!DockerIsAvailable())
        {
            SkipReason = "Docker is not available (no /var/run/docker.sock and no DOCKER_HOST).";
            return;
        }

        var repoRoot = RepoRoot.TryFind();
        if (repoRoot is null)
        {
            SkipReason = "Repository root (folder containing AgentMessageBoard.sln/.slnx) not found above the test binaries.";
            return;
        }

        var realmPath = Path.Combine(repoRoot, "keycloak", "realm.json");
        if (!File.Exists(realmPath))
        {
            SkipReason = $"Realm export not found at {realmPath}.";
            return;
        }

        _container = new KeycloakBuilder(Image)
            .WithRealm(realmPath)
            .Build();

        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await _container.StartAsync(startup.Token);

        BaseAddress = _container.GetBaseAddress().TrimEnd('/') + "/";
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Resource Owner Password grant on the public client; Keycloak derives <c>iss</c> from the Host we call.</summary>
    public async Task<string> PasswordGrantAsync(string username, string password, CancellationToken cancellationToken)
    {
        using var http = NewHttpClient();
        using var response = await http.PostAsync(
            $"{RealmUrl}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = PublicClientId,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid",
            }),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Token request failed with {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Anonymous RFC 7591 registration with the metadata an MCP client such as Claude Code sends: a public client with the
    /// authorization code and refresh token grants and a loopback redirect URI. Returns the issued <c>client_id</c>.
    /// </summary>
    public async Task<string> RegisterClientAsync(string callbackUri, CancellationToken cancellationToken)
    {
        using var http = NewHttpClient();
        using var response = await http.PostAsJsonAsync(
            $"{RealmUrl}/clients-registrations/openid-connect",
            new Dictionary<string, object>
            {
                ["client_name"] = "e2e dynamic client",
                ["redirect_uris"] = new[] { callbackUri },
                ["grant_types"] = RegistrationGrantTypes,
                ["response_types"] = RegistrationResponseTypes,
                ["token_endpoint_auth_method"] = "none",
            },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Client registration failed with {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("client_id").GetString()!;
    }

    /// <summary>
    /// GET on the authorization endpoint without following redirects: 200 is Keycloak's login page, 302 carries an OAuth error
    /// (such as <c>invalid_scope</c>) back to the callback.
    /// </summary>
    public async Task<(HttpStatusCode Status, Uri? Location)> AuthorizeAsync(string clientId, string callbackUri, string scope, CancellationToken cancellationToken)
    {
        using var http = NewHttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await http.GetAsync(AuthorizationUrl(clientId, callbackUri, scope, Pkce.Create()), cancellationToken);
        return (response.StatusCode, response.Headers.Location);
    }

    /// <summary>
    /// The browser flow an agent performs, without a browser: authorization request with PKCE S256, Keycloak's login form,
    /// the redirect to the loopback callback carrying the code, and the code exchange.
    /// </summary>
    public async Task<TokenResponse> LoginWithAuthorizationCodeAsync(
        string clientId,
        string callbackUri,
        string scope,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var pkce = Pkce.Create();
        using var http = NewHttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        using var loginPage = await http.GetAsync(AuthorizationUrl(clientId, callbackUri, scope, pkce), cancellationToken);
        Assert.True(
            loginPage.StatusCode == HttpStatusCode.OK,
            $"Authorization request was not answered with the login page: {(int)loginPage.StatusCode} {loginPage.Headers.Location}");

        var form = LoginFormAction().Match(await loginPage.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(form.Success, "Keycloak's login form was not found in the authorization response.");

        // Keycloak flags its auth-session cookies Secure even on plain HTTP, so HttpClient's cookie jar would withhold them from
        // the next request. A browser treats localhost as a secure context and sends them; do the same by hand.
        using var credentials = new HttpRequestMessage(HttpMethod.Post, WebUtility.HtmlDecode(form.Groups["action"].Value))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password, ["credentialId"] = "" }),
        };
        credentials.Headers.Add("Cookie", string.Join("; ", SessionCookies(loginPage)));

        using var login = await http.SendAsync(credentials, cancellationToken);
        if (login.StatusCode != HttpStatusCode.Found || login.Headers.Location is null)
        {
            var text = Regex.Replace(Regex.Replace(await login.Content.ReadAsStringAsync(cancellationToken), "<script.*?</script>", "", RegexOptions.Singleline), "<[^>]+>", " ");
            text = Regex.Replace(text, "\\s+", " ").Trim();
            Assert.Fail($"Login did not redirect to the callback: {(int)login.StatusCode}. Page text: {text[..Math.Min(text.Length, 500)]}");
        }

        var query = QueryHelpers.ParseQuery(login.Headers.Location!.Query);
        Assert.True(query.TryGetValue("code", out var code), $"Callback carries no authorization code: {login.Headers.Location}");

        using var tokens = await http.PostAsync(
            $"{RealmUrl}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code.ToString(),
                ["redirect_uri"] = callbackUri,
                ["code_verifier"] = pkce.Verifier,
            }),
            cancellationToken);

        var body = await tokens.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(tokens.StatusCode == HttpStatusCode.OK, $"Code exchange failed with {(int)tokens.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        return new TokenResponse(
            json.RootElement.GetProperty("access_token").GetString()!,
            json.RootElement.GetProperty("refresh_token").GetString()!,
            json.RootElement.GetProperty("scope").GetString()!);
    }

    private string AuthorizationUrl(string clientId, string callbackUri, string scope, Pkce pkce) =>
        QueryHelpers.AddQueryString($"{RealmUrl}/protocol/openid-connect/auth", new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = callbackUri,
            ["scope"] = scope,
            ["state"] = Guid.NewGuid().ToString("N"),
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
        });

    /// <summary>The <c>name=value</c> pairs of every cookie a response set, attributes dropped.</summary>
    private static IEnumerable<string> SessionCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Select(v => v.Split(';', 2)[0]) : [];

    private static HttpClient NewHttpClient(HttpMessageHandler? handler = null) =>
        handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(60) }
            : new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };

    private static bool DockerIsAvailable() =>
        File.Exists("/var/run/docker.sock") || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"));

    // Keycloak's login.ftl renders <form id="kc-form-login" ... action="..." method="post">; accept either attribute order.
    [GeneratedRegex("<form[^>]*id=\"kc-form-login\"[^>]*action=\"(?<action>[^\"]+)\"|<form[^>]*action=\"(?<action>[^\"]+)\"[^>]*id=\"kc-form-login\"", RegexOptions.IgnoreCase)]
    private static partial Regex LoginFormAction();

    private sealed record Pkce(string Verifier, string Challenge)
    {
        public static Pkce Create()
        {
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            return new Pkce(verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        }
    }
}
