using AgentMessageBoard.Server.Data;
using AgentMessageBoard.Server.Domain;
using AgentMessageBoard.Server.Services;
using AgentMessageBoard.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgentMessageBoard.Tests;

/// <summary>Unit tests for <see cref="MessageService"/> against an in-memory SQLite database and a controllable clock.</summary>
public sealed class MessageServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly AgentIdentity Alice = new("alice-subject-id", "alice", "test-agent/1.2.3");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestTimeProvider _clock = new(Start);
    private MessageBoardDbContext _db = null!;
    private MessageService _service = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Cancellation.Token);
        _db = new MessageBoardDbContext(new DbContextOptionsBuilder<MessageBoardDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync(Cancellation.Token);
        _service = new MessageService(_db, _clock);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Post_stores_trimmed_content_with_identity_and_clock_time()
    {
        var posted = await _service.PostAsync(MessageLabel.Fix, "  fixed the thing \r\n", Alice, Cancellation.Token);

        Assert.Equal("fixed the thing", posted.Content);
        Assert.Equal(MessageLabel.Fix, posted.Label);
        Assert.Equal("test-agent/1.2.3", posted.UserAgent);
        Assert.Equal("alice", posted.PostedBy);
        Assert.Equal(Start.UtcDateTime, posted.CreatedAtUtc);
        Assert.True(posted.Id > 0);

        var row = Assert.Single(await _db.Messages.AsNoTracking().ToListAsync(Cancellation.Token));
        Assert.Equal("fixed the thing", row.Content);
        Assert.Equal("alice-subject-id", row.PostedById);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Post_rejects_empty_or_whitespace_content(string content)
    {
        var ex = await Assert.ThrowsAsync<MessageValidationException>(() => _service.PostAsync(MessageLabel.Activity, content, Alice, Cancellation.Token));

        Assert.Equal("content must not be empty.", ex.Message);
        Assert.Empty(await _db.Messages.ToListAsync(Cancellation.Token));
    }

    [Fact]
    public async Task Post_rejects_content_over_4000_characters_and_reports_the_count()
    {
        var content = new string('a', 4001);

        var ex = await Assert.ThrowsAsync<MessageValidationException>(() => _service.PostAsync(MessageLabel.Activity, content, Alice, Cancellation.Token));

        Assert.Contains("4000", ex.Message);
        Assert.Contains("4001", ex.Message);
        Assert.Empty(await _db.Messages.ToListAsync(Cancellation.Token));
    }

    [Fact]
    public async Task Post_counts_length_after_trimming()
    {
        var content = "  " + new string('a', 4000) + "  ";

        var posted = await _service.PostAsync(MessageLabel.Activity, content, Alice, Cancellation.Token);

        Assert.Equal(4000, posted.Content.Length);
    }

    [Fact]
    public async Task Post_rejects_an_undefined_label()
    {
        var ex = await Assert.ThrowsAsync<MessageValidationException>(() => _service.PostAsync((MessageLabel)42, "hello", Alice, Cancellation.Token));

        Assert.Contains("activity, help_required, fix", ex.Message);
    }

    [Fact]
    public async Task Post_truncates_an_over_long_user_agent_to_200_characters()
    {
        var longAgent = new string('u', 250);

        var posted = await _service.PostAsync(MessageLabel.Activity, "hello", Alice with { UserAgent = longAgent }, Cancellation.Token);

        Assert.Equal(200, posted.UserAgent.Length);
        Assert.Equal(new string('u', 200), posted.UserAgent);
    }

    [Fact]
    public async Task Post_truncates_over_long_identity_fields_to_200_characters()
    {
        var identity = new AgentIdentity(new string('i', 300), new string('n', 300), "agent/1");

        var posted = await _service.PostAsync(MessageLabel.Activity, "hello", identity, Cancellation.Token);

        Assert.Equal(200, posted.PostedBy.Length);
        var row = Assert.Single(await _db.Messages.AsNoTracking().ToListAsync(Cancellation.Token));
        Assert.Equal(200, row.PostedById.Length);
    }

    [Fact]
    public async Task Post_substitutes_unknown_for_blank_identity_fields()
    {
        var posted = await _service.PostAsync(MessageLabel.Activity, "hello", new AgentIdentity("", "  ", ""), Cancellation.Token);

        Assert.Equal("unknown", posted.UserAgent);
        Assert.Equal("unknown", posted.PostedBy);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    public void Query_limit_is_clamped_to_1_through_100(int? limit, int expected)
    {
        Assert.Equal(expected, new MessageQuery(Limit: limit).EffectiveLimit);
    }

    [Fact]
    public async Task List_applies_the_clamped_limit()
    {
        for (var i = 0; i < 25; i++)
        {
            await _service.PostAsync(MessageLabel.Activity, $"m{i}", Alice, Cancellation.Token);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Single(await _service.ListAsync(new MessageQuery(Limit: 0), Cancellation.Token));
        Assert.Equal(20, (await _service.ListAsync(new MessageQuery(), Cancellation.Token)).Count);
        Assert.Equal(25, (await _service.ListAsync(new MessageQuery(Limit: 1000), Cancellation.Token)).Count);
    }

    [Fact]
    public async Task List_returns_newest_first_by_clock_time_regardless_of_insertion_order()
    {
        _clock.SetUtcNow(Start.AddHours(2));
        var middle = await _service.PostAsync(MessageLabel.Activity, "inserted first, created second", Alice, Cancellation.Token);
        _clock.SetUtcNow(Start.AddHours(3));
        var newest = await _service.PostAsync(MessageLabel.Activity, "inserted second, created last", Alice, Cancellation.Token);
        _clock.SetUtcNow(Start.AddHours(1));
        var oldest = await _service.PostAsync(MessageLabel.Activity, "inserted last, created first", Alice, Cancellation.Token);

        var listed = await _service.ListAsync(new MessageQuery(), Cancellation.Token);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], listed.Select(m => m.Id));
        Assert.Equal([Start.AddHours(3).UtcDateTime, Start.AddHours(2).UtcDateTime, Start.AddHours(1).UtcDateTime], listed.Select(m => m.CreatedAtUtc));
    }

    [Fact]
    public async Task List_breaks_timestamp_ties_by_newest_id()
    {
        var a = await _service.PostAsync(MessageLabel.Activity, "a", Alice, Cancellation.Token);
        var b = await _service.PostAsync(MessageLabel.Activity, "b", Alice, Cancellation.Token);
        var c = await _service.PostAsync(MessageLabel.Activity, "c", Alice, Cancellation.Token);

        var listed = await _service.ListAsync(new MessageQuery(), Cancellation.Token);

        Assert.Equal([c.Id, b.Id, a.Id], listed.Select(m => m.Id));
    }

    [Fact]
    public async Task List_filters_by_label()
    {
        await _service.PostAsync(MessageLabel.Activity, "a", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "f", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.HelpRequired, "h", Alice, Cancellation.Token);

        var fixes = await _service.ListAsync(new MessageQuery(Label: MessageLabel.Fix), Cancellation.Token);

        var only = Assert.Single(fixes);
        Assert.Equal("f", only.Content);
    }

    [Fact]
    public async Task List_reads_timestamps_back_from_sqlite_as_utc_kind()
    {
        // SQLite stores TEXT without a zone; without the value converter EF materializes Kind=Unspecified and the JSON loses its "Z".
        await _service.PostAsync(MessageLabel.Activity, "hello", Alice, Cancellation.Token);

        var listed = Assert.Single(await _service.ListAsync(new MessageQuery(), Cancellation.Token));

        Assert.Equal(DateTimeKind.Utc, listed.CreatedAtUtc.Kind);
        Assert.Equal(Start.UtcDateTime, listed.CreatedAtUtc);
    }

    [Fact]
    public async Task List_since_is_inclusive_and_treats_unspecified_as_utc_and_local_as_shifted()
    {
        await _service.PostAsync(MessageLabel.Activity, "before", Alice, Cancellation.Token);
        _clock.Advance(TimeSpan.FromMinutes(10));
        var cutoff = _clock.GetUtcNow().UtcDateTime;
        await _service.PostAsync(MessageLabel.Activity, "at", Alice, Cancellation.Token);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await _service.PostAsync(MessageLabel.Activity, "after", Alice, Cancellation.Token);

        var utc = await _service.ListAsync(new MessageQuery(SinceUtc: cutoff), Cancellation.Token);
        var unspecified = await _service.ListAsync(new MessageQuery(SinceUtc: DateTime.SpecifyKind(cutoff, DateTimeKind.Unspecified)), Cancellation.Token);
        var local = await _service.ListAsync(new MessageQuery(SinceUtc: cutoff.ToLocalTime()), Cancellation.Token);

        Assert.Equal(["after", "at"], utc.Select(m => m.Content));
        Assert.Equal(["after", "at"], unspecified.Select(m => m.Content));
        Assert.Equal(["after", "at"], local.Select(m => m.Content));
    }

    [Fact]
    public void Query_normalizes_since_to_a_utc_instant()
    {
        var instant = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Null(new MessageQuery().EffectiveSinceUtc);
        Assert.Equal(instant, new MessageQuery(SinceUtc: instant).EffectiveSinceUtc);
        Assert.Equal(instant, new MessageQuery(SinceUtc: DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)).EffectiveSinceUtc);
        Assert.Equal(instant, new MessageQuery(SinceUtc: instant.ToLocalTime()).EffectiveSinceUtc);
        Assert.All(
            new DateTime?[] { instant, DateTime.SpecifyKind(instant, DateTimeKind.Unspecified), instant.ToLocalTime() },
            since => Assert.Equal(DateTimeKind.Utc, new MessageQuery(SinceUtc: since).EffectiveSinceUtc!.Value.Kind));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  redis ", "redis")]
    public void Query_normalizes_search_to_a_trimmed_term_or_none(string? search, string? expected)
    {
        Assert.Equal(expected, new MessageQuery(Search: search).EffectiveSearch);
    }

    [Fact]
    public async Task Search_is_case_insensitive_and_trims_the_term()
    {
        await _service.PostAsync(MessageLabel.Fix, "Connection refused by Redis", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "redis cluster resharded", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "nothing to see", Alice, Cancellation.Token);

        var hits = await _service.ListAsync(new MessageQuery(Search: "  REDIS "), Cancellation.Token);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, m => Assert.Contains("redis", m.Content, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_escapes_percent_underscore_and_backslash_so_they_match_literally()
    {
        await _service.PostAsync(MessageLabel.Fix, "cpu at 100% for an hour", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "cpu at 100 for an hour", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "use snake_case names", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "use snakeXcase names", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, @"escaped \n in logs", Alice, Cancellation.Token);
        await _service.PostAsync(MessageLabel.Fix, "escaped n in logs", Alice, Cancellation.Token);

        var percent = await _service.ListAsync(new MessageQuery(Search: "100%"), Cancellation.Token);
        var underscore = await _service.ListAsync(new MessageQuery(Search: "snake_case"), Cancellation.Token);
        var backslash = await _service.ListAsync(new MessageQuery(Search: @"\n"), Cancellation.Token);
        var lonePercent = await _service.ListAsync(new MessageQuery(Search: "%"), Cancellation.Token);

        Assert.Equal(["cpu at 100% for an hour"], percent.Select(m => m.Content));
        Assert.Equal(["use snake_case names"], underscore.Select(m => m.Content));
        Assert.Equal([@"escaped \n in logs"], backslash.Select(m => m.Content));
        Assert.Equal(["cpu at 100% for an hour"], lonePercent.Select(m => m.Content));
    }

    [Fact]
    public async Task Blank_search_is_ignored()
    {
        await _service.PostAsync(MessageLabel.Fix, "anything", Alice, Cancellation.Token);

        var all = await _service.ListAsync(new MessageQuery(Search: "   "), Cancellation.Token);

        Assert.Single(all);
    }
}
