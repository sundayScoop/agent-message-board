using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentMessageBoard.Server.Domain;
using AgentMessageBoard.Tests.Infrastructure;

namespace AgentMessageBoard.Tests;

/// <summary>REST read endpoint. Rows are seeded straight into the database so timestamps and ordering are exact.</summary>
public sealed class MessagesApiTests : IAsyncDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly MessageBoardFactory _factory = new();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static Message Row(MessageLabel label, string content, DateTime createdAtUtc, string postedBy = "alice") => new()
    {
        Label = label,
        Content = content,
        UserAgent = "seed/1.0",
        PostedById = TestTokens.SubjectFor(postedBy),
        PostedBy = postedBy,
        CreatedAtUtc = createdAtUtc,
    };

    private async Task<IReadOnlyList<MessageRecord>> GetAsync(string query = "")
    {
        using var client = _factory.CreateClient(TestTokens.Valid());
        var result = await client.GetFromJsonAsync<List<MessageRecord>>($"/api/messages{query}", MessageJson.Options, Cancellation.Token);
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public async Task Returns_newest_first_with_default_limit_of_20()
    {
        await _factory.SeedAsync([.. Enumerable.Range(1, 25).Select(i => Row(MessageLabel.Activity, $"m{i}", T0.AddMinutes(i)))]);

        var messages = await GetAsync();

        Assert.Equal(20, messages.Count);
        Assert.Equal("m25", messages[0].Content);
        Assert.Equal("m6", messages[^1].Content);
        Assert.Equal(messages.OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id), messages);
    }

    [Fact]
    public async Task Response_has_the_documented_camel_case_shape()
    {
        var seeded = await _factory.SeedAsync(Row(MessageLabel.HelpRequired, "shape check", T0, postedBy: "bob"));
        using var client = _factory.CreateClient(TestTokens.Valid());

        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/messages", Cancellation.Token));
        var item = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(
            ["content", "createdAtUtc", "id", "label", "postedBy", "userAgent"],
            item.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(seeded[0].Id, item.GetProperty("id").GetInt64());
        Assert.Equal("help_required", item.GetProperty("label").GetString());
        Assert.Equal("shape check", item.GetProperty("content").GetString());
        Assert.Equal("seed/1.0", item.GetProperty("userAgent").GetString());
        Assert.Equal("bob", item.GetProperty("postedBy").GetString());
        Assert.Equal("2026-01-01T12:00:00Z", item.GetProperty("createdAtUtc").GetString());
    }

    [Fact]
    public async Task CreatedAtUtc_read_back_from_sqlite_is_utc_with_a_z_suffix()
    {
        // Regression: SQLite stores TEXT without a zone and EF materializes Kind=Unspecified, which serialized without the "Z".
        await _factory.SeedAsync(Row(MessageLabel.Fix, "utc check", T0));
        using var client = _factory.CreateClient(TestTokens.Valid());

        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/messages", Cancellation.Token));
        var typed = await GetAsync();

        var raw = Assert.Single(document.RootElement.EnumerateArray()).GetProperty("createdAtUtc").GetString();
        Assert.EndsWith("Z", raw);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(typed).CreatedAtUtc.Kind);
        Assert.Equal(T0, Assert.Single(typed).CreatedAtUtc);
    }

    [Theory]
    [InlineData("fix", "fix")]
    [InlineData("help_required", "help_required")]
    [InlineData("help-required", "help_required")]
    [InlineData("HelpRequired", "help_required")]
    [InlineData("ACTIVITY", "activity")]
    public async Task Label_filter_accepts_wire_names_leniently(string queryValue, string expectedLabel)
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Activity, "a", T0.AddMinutes(1)),
            Row(MessageLabel.HelpRequired, "h", T0.AddMinutes(2)),
            Row(MessageLabel.Fix, "f", T0.AddMinutes(3)));

        var messages = await GetAsync($"?label={Uri.EscapeDataString(queryValue)}");

        var only = Assert.Single(messages);
        Assert.Equal(expectedLabel, only.Label);
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("fixes")]
    [InlineData("help%20required")]
    public async Task Invalid_label_is_a_400_validation_problem(string label)
    {
        using var client = _factory.CreateClient(TestTokens.Valid());

        using var response = await client.GetAsync($"/api/messages?label={label}", Cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("label", out var labelErrors), $"Expected errors.label: {problem.RootElement}");
        var message = Assert.Single(labelErrors.EnumerateArray()).GetString();
        Assert.Contains("activity", message);
        Assert.Contains("help_required", message);
        Assert.Contains("fix", message);
    }

    [Theory]
    [InlineData("?limit=0", 1)]
    [InlineData("?limit=-5", 1)]
    [InlineData("?limit=1", 1)]
    [InlineData("?limit=7", 7)]
    [InlineData("?limit=100", 100)]
    [InlineData("?limit=1000", 100)]
    [InlineData("", 20)]
    public async Task Limit_is_clamped_to_1_through_100(string query, int expectedCount)
    {
        await _factory.SeedAsync([.. Enumerable.Range(1, 105).Select(i => Row(MessageLabel.Fix, $"m{i}", T0.AddSeconds(i)))]);

        var messages = await GetAsync(query);

        Assert.Equal(expectedCount, messages.Count);
        Assert.Equal("m105", messages[0].Content);
    }

    [Fact]
    public async Task Since_filter_is_inclusive_and_uses_utc()
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Activity, "before", T0.AddHours(-1)),
            Row(MessageLabel.Activity, "exactly", T0),
            Row(MessageLabel.Activity, "after", T0.AddHours(1)));

        var fromT0 = await GetAsync($"?since={Uri.EscapeDataString(T0.ToString("O"))}");
        var fromLater = await GetAsync($"?since={Uri.EscapeDataString(T0.AddMinutes(1).ToString("O"))}");
        var fromFuture = await GetAsync("?since=2099-01-01T00:00:00Z");

        Assert.Equal(["after", "exactly"], fromT0.Select(m => m.Content));
        Assert.Equal(["after"], fromLater.Select(m => m.Content));
        Assert.Empty(fromFuture);
    }

    [Fact]
    public async Task Since_without_a_zone_designator_is_read_as_utc_and_offsets_are_honoured()
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Activity, "before", T0.AddHours(-1)),
            Row(MessageLabel.Activity, "exactly", T0),
            Row(MessageLabel.Activity, "after", T0.AddHours(1)));

        var noDesignator = await GetAsync("?since=2026-01-01T12:00:00");
        var withOffset = await GetAsync($"?since={Uri.EscapeDataString("2026-01-01T13:00:00+01:00")}");

        Assert.Equal(["after", "exactly"], noDesignator.Select(m => m.Content));
        Assert.Equal(["after", "exactly"], withOffset.Select(m => m.Content));
    }

    [Fact]
    public async Task Search_is_a_case_insensitive_substring_match()
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Fix, "Timeout talking to Redis", T0.AddMinutes(1)),
            Row(MessageLabel.Activity, "Migrating the cache to REDIS cluster", T0.AddMinutes(2)),
            Row(MessageLabel.HelpRequired, "Postgres deadlock", T0.AddMinutes(3)));

        var hits = await GetAsync("?q=redis");

        Assert.Equal(["Migrating the cache to REDIS cluster", "Timeout talking to Redis"], hits.Select(m => m.Content));
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Fix, "coverage at 100% now", T0.AddMinutes(1)),
            Row(MessageLabel.Fix, "coverage at 100 now", T0.AddMinutes(2)),
            Row(MessageLabel.Fix, "snake_case identifiers", T0.AddMinutes(3)),
            Row(MessageLabel.Fix, "snakeXcase identifiers", T0.AddMinutes(4)),
            Row(MessageLabel.Fix, @"path C:\temp", T0.AddMinutes(5)));

        var percent = await GetAsync($"?q={Uri.EscapeDataString("100%")}");
        var underscore = await GetAsync($"?q={Uri.EscapeDataString("snake_case")}");
        var backslash = await GetAsync($"?q={Uri.EscapeDataString(@"C:\temp")}");

        Assert.Equal(["coverage at 100% now"], percent.Select(m => m.Content));
        Assert.Equal(["snake_case identifiers"], underscore.Select(m => m.Content));
        Assert.Equal([@"path C:\temp"], backslash.Select(m => m.Content));
    }

    [Fact]
    public async Task Filters_combine()
    {
        await _factory.SeedAsync(
            Row(MessageLabel.Fix, "redis fix old", T0.AddHours(-2)),
            Row(MessageLabel.Fix, "redis fix new", T0.AddHours(1)),
            Row(MessageLabel.Activity, "redis activity new", T0.AddHours(2)),
            Row(MessageLabel.Fix, "postgres fix new", T0.AddHours(3)),
            Row(MessageLabel.Fix, "redis fix newest", T0.AddHours(4)));

        var messages = await GetAsync($"?label=fix&q=redis&since={Uri.EscapeDataString(T0.ToString("O"))}&limit=1");

        Assert.Equal(["redis fix newest"], messages.Select(m => m.Content));
    }

    [Fact]
    public async Task Empty_board_returns_an_empty_array()
    {
        var messages = await GetAsync();

        Assert.Empty(messages);
    }
}
