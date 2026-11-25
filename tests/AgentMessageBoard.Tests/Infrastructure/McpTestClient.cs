using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>A real MCP client (Streamable HTTP) connected to the in-memory host, authenticating with a bearer token.</summary>
internal sealed class McpTestClient : IAsyncDisposable
{
    public const string DefaultClientName = "test-agent";
    public const string DefaultClientVersion = "1.2.3";

    private readonly HttpClientTransport _transport;

    private McpTestClient(McpClient client, HttpClientTransport transport)
    {
        Client = client;
        _transport = transport;
    }

    public McpClient Client { get; }

    public static async Task<McpTestClient> ConnectAsync(
        MessageBoardFactory factory,
        string bearerToken,
        string clientName = DefaultClientName,
        string clientVersion = DefaultClientVersion,
        CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(factory.Server.BaseAddress, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
            },
            factory.CreateClient(),
            loggerFactory: null,
            ownsHttpClient: true);

        var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ClientInfo = new Implementation { Name = clientName, Version = clientVersion } },
            cancellationToken: cancellationToken);

        return new McpTestClient(client, transport);
    }

    public ValueTask<CallToolResult> CallAsync(string tool, Dictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default) =>
        Client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);

    /// <summary>Calls <c>post_message</c> and returns the structured result; fails the test if the tool reported an error.</summary>
    public async Task<MessageRecord> PostAsync(string label, string content, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("post_message", new() { ["label"] = label, ["content"] = content }, cancellationToken);
        AssertSuccess(result);
        return StructuredObject(result).ToMessage();
    }

    public async Task<IReadOnlyList<MessageRecord>> ListAsync(string? label = null, int? limit = null, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>();
        if (label is not null)
        {
            args["label"] = label;
        }

        if (limit is not null)
        {
            args["limit"] = limit;
        }

        if (since is not null)
        {
            // Round-trip format: Kind=Utc gets a "Z", Unspecified no designator, Local its offset.
            args["since"] = since.Value.ToString("O");
        }

        var result = await CallAsync("list_messages", args, cancellationToken);
        AssertSuccess(result);
        return StructuredList(result);
    }

    public async Task<IReadOnlyList<MessageRecord>> SearchAsync(string query, string? label = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["query"] = query };
        if (label is not null)
        {
            args["label"] = label;
        }

        if (limit is not null)
        {
            args["limit"] = limit;
        }

        var result = await CallAsync("search_messages", args, cancellationToken);
        AssertSuccess(result);
        return StructuredList(result);
    }

    public static void AssertSuccess(CallToolResult result)
    {
        if (result.IsError == true)
        {
            Assert.Fail($"Tool call reported an error: {ErrorText(result)}");
        }
    }

    /// <summary>Concatenated text content of a result; for errors this is the message the model would see.</summary>
    public static string ErrorText(CallToolResult result) =>
        string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    public static JsonElement StructuredObject(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent.Value;
    }

    /// <summary>
    /// A tool returning a list has its structured content wrapped in an object (the MCP spec requires an object),
    /// so accept either the bare array or the SDK's <c>{"result": [...]}</c> envelope.
    /// </summary>
    public static IReadOnlyList<MessageRecord> StructuredList(CallToolResult result)
    {
        var element = StructuredObject(result);
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.ToMessages();
        }

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.True(element.TryGetProperty("result", out var inner), $"Expected a 'result' array in structured content: {element}");
        return inner.ToMessages();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _transport.DisposeAsync();
    }
}
