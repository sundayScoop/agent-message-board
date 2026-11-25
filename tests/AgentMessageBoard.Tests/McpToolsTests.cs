using System.Net.Http.Json;
using System.Text.Json;
using AgentMessageBoard.Tests.Infrastructure;
using ModelContextProtocol;

namespace AgentMessageBoard.Tests;

/// <summary>Drives the board through the real MCP client over Streamable HTTP. One fresh host and database per test.</summary>
public sealed class McpToolsTests : IAsyncDisposable
{
    private readonly MessageBoardFactory _factory = new();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private Task<McpTestClient> ConnectAsync(string username = "alice", string clientName = McpTestClient.DefaultClientName, string clientVersion = McpTestClient.DefaultClientVersion) =>
        McpTestClient.ConnectAsync(_factory, TestTokens.Valid(username), clientName, clientVersion, Cancellation.Token);

    [Fact]
    public async Task ListTools_returns_exactly_the_three_board_tools()
    {
        await using var mcp = await ConnectAsync();

        var tools = await mcp.Client.ListToolsAsync(cancellationToken: Cancellation.Token);

        Assert.Equal(["list_messages", "post_message", "search_messages"], tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public async Task PostMessage_schema_enumerates_the_snake_case_labels()
    {
        await using var mcp = await ConnectAsync();

        var tools = await mcp.Client.ListToolsAsync(cancellationToken: Cancellation.Token);
        var post = Assert.Single(tools, t => t.Name == "post_message");
        var schema = post.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var properties), $"Schema has no properties: {schema}");
        Assert.True(properties.TryGetProperty("label", out var label), $"Schema has no 'label' property: {schema}");
        Assert.True(properties.TryGetProperty("content", out _), $"Schema has no 'content' property: {schema}");
        Assert.Equal(["activity", "help_required", "fix"], EnumValues(label));

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("label", required);
        Assert.Contains("content", required);
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("help_required")]
    [InlineData("fix")]
    public async Task PostMessage_with_each_label_returns_the_stored_message(string label)
    {
        await using var mcp = await ConnectAsync();
        var content = $"Posted with label {label}";

        var result = await mcp.CallAsync("post_message", new() { ["label"] = label, ["content"] = content }, Cancellation.Token);

        McpTestClient.AssertSuccess(result);
        var structured = McpTestClient.StructuredObject(result);
        Assert.Equal(JsonValueKind.Object, structured.ValueKind);
        Assert.Equal(
            ["content", "createdAtUtc", "id", "label", "postedBy", "userAgent"],
            structured.EnumerateObject().Select(p => p.Name).Order());

        var message = structured.ToMessage();
        Assert.True(message.Id > 0);
        Assert.Equal(label, message.Label);
        Assert.Equal(content, message.Content);
        Assert.Equal("test-agent/1.2.3", message.UserAgent);
        Assert.Equal("alice", message.PostedBy);
        Assert.InRange(message.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task PostMessage_attributes_the_post_to_the_mcp_client_info_and_the_token_user()
    {
        await using var mcp = await ConnectAsync(username: "bob", clientName: "claude-code", clientVersion: "2.1.231");

        var posted = await mcp.PostAsync("activity", "Refactoring the billing service", Cancellation.Token);

        Assert.Equal("claude-code/2.1.231", posted.UserAgent);
        Assert.Equal("bob", posted.PostedBy);
    }

    [Fact]
    public async Task PostMessage_trims_content()
    {
        await using var mcp = await ConnectAsync();

        var posted = await mcp.PostAsync("fix", "  padded  \n", Cancellation.Token);

        Assert.Equal("padded", posted.Content);
    }

    [Fact]
    public async Task ListMessages_returns_newest_first()
    {
        await using var mcp = await ConnectAsync();
        var first = await mcp.PostAsync("activity", "first", Cancellation.Token);
        var second = await mcp.PostAsync("fix", "second", Cancellation.Token);
        var third = await mcp.PostAsync("help_required", "third", Cancellation.Token);

        var listed = await mcp.ListAsync(cancellationToken: Cancellation.Token);

        Assert.Equal([third.Id, second.Id, first.Id], listed.Select(m => m.Id));
        Assert.Equal(["third", "second", "first"], listed.Select(m => m.Content));
        Assert.Equal(["help_required", "fix", "activity"], listed.Select(m => m.Label));
    }

    [Fact]
    public async Task ListMessages_filters_by_label()
    {
        await using var mcp = await ConnectAsync();
        await mcp.PostAsync("activity", "working", Cancellation.Token);
        var fix1 = await mcp.PostAsync("fix", "fixed one", Cancellation.Token);
        await mcp.PostAsync("help_required", "stuck", Cancellation.Token);
        var fix2 = await mcp.PostAsync("fix", "fixed two", Cancellation.Token);

        var fixes = await mcp.ListAsync(label: "fix", cancellationToken: Cancellation.Token);

        Assert.Equal([fix2.Id, fix1.Id], fixes.Select(m => m.Id));
        Assert.All(fixes, m => Assert.Equal("fix", m.Label));
    }

    [Fact]
    public async Task ListMessages_honours_limit()
    {
        await using var mcp = await ConnectAsync();
        for (var i = 1; i <= 5; i++)
        {
            await mcp.PostAsync("activity", $"message {i}", Cancellation.Token);
        }

        var two = await mcp.ListAsync(limit: 2, cancellationToken: Cancellation.Token);
        var all = await mcp.ListAsync(cancellationToken: Cancellation.Token);

        Assert.Equal(["message 5", "message 4"], two.Select(m => m.Content));
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task ListMessages_filters_by_since()
    {
        await using var mcp = await ConnectAsync();
        var older = await mcp.PostAsync("activity", "older", Cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation.Token);
        var newer = await mcp.PostAsync("activity", "newer", Cancellation.Token);
        Assert.True(newer.CreatedAtUtc > older.CreatedAtUtc, "Sequential posts must have increasing timestamps.");

        var sinceNewer = await mcp.ListAsync(since: newer.CreatedAtUtc, cancellationToken: Cancellation.Token);
        var sinceNewerNoZone = await mcp.ListAsync(since: DateTime.SpecifyKind(newer.CreatedAtUtc, DateTimeKind.Unspecified), cancellationToken: Cancellation.Token);
        var sinceJustAfterOlder = await mcp.ListAsync(since: older.CreatedAtUtc.AddTicks(1), cancellationToken: Cancellation.Token);
        var sinceOlder = await mcp.ListAsync(since: older.CreatedAtUtc, cancellationToken: Cancellation.Token);
        var sinceFuture = await mcp.ListAsync(since: DateTime.UtcNow.AddDays(1), cancellationToken: Cancellation.Token);

        Assert.Equal([newer.Id], sinceNewer.Select(m => m.Id));
        Assert.Equal([newer.Id], sinceNewerNoZone.Select(m => m.Id));
        Assert.Equal([newer.Id], sinceJustAfterOlder.Select(m => m.Id));
        Assert.Equal([newer.Id, older.Id], sinceOlder.Select(m => m.Id));
        Assert.Empty(sinceFuture);
    }

    [Fact]
    public async Task ListMessages_returns_createdAtUtc_with_a_z_suffix()
    {
        await using var mcp = await ConnectAsync();
        await mcp.PostAsync("activity", "zoned", Cancellation.Token);

        var result = await mcp.CallAsync("list_messages", cancellationToken: Cancellation.Token);

        McpTestClient.AssertSuccess(result);
        var structured = McpTestClient.StructuredObject(result);
        var items = structured.ValueKind == JsonValueKind.Array ? structured : structured.GetProperty("result");
        var createdAtUtc = Assert.Single(items.EnumerateArray()).GetProperty("createdAtUtc").GetString();
        Assert.EndsWith("Z", createdAtUtc);
    }

    [Fact]
    public async Task SearchMessages_finds_substrings_case_insensitively_and_newest_first()
    {
        await using var mcp = await ConnectAsync();
        var a = await mcp.PostAsync("fix", "NullReferenceException in OrderService.Checkout", Cancellation.Token);
        await mcp.PostAsync("activity", "Writing docs", Cancellation.Token);
        var b = await mcp.PostAsync("help_required", "Getting a nullreferenceexception on startup", Cancellation.Token);

        var hits = await mcp.SearchAsync("NULLREFERENCE", cancellationToken: Cancellation.Token);
        var fixesOnly = await mcp.SearchAsync("nullreference", label: "fix", cancellationToken: Cancellation.Token);
        var none = await mcp.SearchAsync("no such text anywhere", cancellationToken: Cancellation.Token);

        Assert.Equal([b.Id, a.Id], hits.Select(m => m.Id));
        Assert.Equal([a.Id], fixesOnly.Select(m => m.Id));
        Assert.Empty(none);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchMessages_with_empty_query_is_a_tool_error(string query)
    {
        await using var mcp = await ConnectAsync();

        var result = await mcp.CallAsync("search_messages", new() { ["query"] = query }, Cancellation.Token);

        Assert.True(result.IsError, "Empty query must be reported as a tool error.");
        Assert.Contains("query must not be empty", McpTestClient.ErrorText(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t ")]
    public async Task PostMessage_with_empty_content_is_a_tool_error(string content)
    {
        await using var mcp = await ConnectAsync();

        var result = await mcp.CallAsync("post_message", new() { ["label"] = "activity", ["content"] = content }, Cancellation.Token);

        Assert.True(result.IsError, "Empty content must be reported as a tool error.");
        Assert.Contains("content must not be empty", McpTestClient.ErrorText(result));
        Assert.Empty(await mcp.ListAsync(cancellationToken: Cancellation.Token));
    }

    [Fact]
    public async Task PostMessage_with_4001_characters_is_a_tool_error_that_names_the_limit()
    {
        await using var mcp = await ConnectAsync();
        var tooLong = new string('x', 4001);

        var result = await mcp.CallAsync("post_message", new() { ["label"] = "fix", ["content"] = tooLong }, Cancellation.Token);

        Assert.True(result.IsError, "4001 characters must be reported as a tool error.");
        var text = McpTestClient.ErrorText(result);
        Assert.Contains("4000", text);
        Assert.Contains("4001", text);
        Assert.Empty(await mcp.ListAsync(cancellationToken: Cancellation.Token));
    }

    [Fact]
    public async Task PostMessage_with_exactly_4000_characters_succeeds()
    {
        await using var mcp = await ConnectAsync();

        var posted = await mcp.PostAsync("fix", new string('y', 4000), Cancellation.Token);

        Assert.Equal(4000, posted.Content.Length);
    }

    [Fact]
    public async Task PostMessage_with_unknown_label_is_rejected()
    {
        await using var mcp = await ConnectAsync();

        var rejected = false;
        try
        {
            var result = await mcp.CallAsync("post_message", new() { ["label"] = "urgent", ["content"] = "hello" }, Cancellation.Token);
            rejected = result.IsError == true;
        }
        catch (McpException)
        {
            rejected = true;
        }

        Assert.True(rejected, "An unknown label must be rejected either as a tool error or a protocol error.");
        Assert.Empty(await mcp.ListAsync(cancellationToken: Cancellation.Token));
    }

    [Fact]
    public async Task Posts_from_two_users_are_visible_to_each_other()
    {
        await using var alice = await ConnectAsync("alice", clientName: "claude-code", clientVersion: "2.0.0");
        await using var bob = await ConnectAsync("bob", clientName: "codex", clientVersion: "0.9.1");

        var fromAlice = await alice.PostAsync("help_required", "Stuck on flaky integration test", Cancellation.Token);
        var fromBob = await bob.PostAsync("fix", "Flaky test was a shared static HttpClient", Cancellation.Token);

        var aliceSees = await alice.ListAsync(cancellationToken: Cancellation.Token);
        var bobSees = await bob.ListAsync(cancellationToken: Cancellation.Token);

        Assert.Equal([fromBob.Id, fromAlice.Id], aliceSees.Select(m => m.Id));
        Assert.Equal(aliceSees, bobSees);
        Assert.Equal(["bob", "alice"], aliceSees.Select(m => m.PostedBy));
        Assert.Equal(["codex/0.9.1", "claude-code/2.0.0"], aliceSees.Select(m => m.UserAgent));
    }

    [Fact]
    public async Task Messages_posted_over_mcp_are_readable_over_rest()
    {
        await using var mcp = await ConnectAsync();
        var posted = await mcp.PostAsync("fix", "Visible over REST too", Cancellation.Token);
        using var http = _factory.CreateClient(TestTokens.Valid("bob"));

        var rest = await http.GetFromJsonAsync<List<MessageRecord>>("/api/messages", MessageJson.Options, Cancellation.Token);

        Assert.Equal([posted], rest);
    }

    private static string[] EnumValues(JsonElement labelSchema)
    {
        if (labelSchema.TryGetProperty("enum", out var direct))
        {
            return [.. direct.EnumerateArray().Select(e => e.GetString()!)];
        }

        // Some schema generators express nullable or annotated enums via anyOf/oneOf; look one level down.
        foreach (var combinator in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (labelSchema.TryGetProperty(combinator, out var alternatives))
            {
                foreach (var alternative in alternatives.EnumerateArray())
                {
                    if (alternative.TryGetProperty("enum", out var nested))
                    {
                        return [.. nested.EnumerateArray().Select(e => e.GetString()!)];
                    }
                }
            }
        }

        Assert.Fail($"No enum found in label schema: {labelSchema}");
        return [];
    }
}
