using AgentMessageBoard.Server.Data;
using AgentMessageBoard.Server.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgentMessageBoard.Server.Services;

/// <summary>All business rules of the board: content limits, identity fallbacks, ordering, filters and search escaping.</summary>
internal sealed class MessageService(MessageBoardDbContext db, TimeProvider timeProvider) : IMessageService
{
    private const string LikeEscape = "\\";

    public async Task<MessageDto> PostAsync(MessageLabel label, string content, AgentIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(label))
        {
            throw new MessageValidationException($"label must be one of: {MessageLabels.All}.");
        }

        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new MessageValidationException("content must not be empty.");
        }

        if (trimmed.Length > Message.MaxContentLength)
        {
            throw new MessageValidationException($"content must be at most {Message.MaxContentLength} characters (got {trimmed.Length}).");
        }

        var message = new Message
        {
            Label = label,
            Content = trimmed,
            UserAgent = FitColumn(identity.UserAgent, Message.MaxUserAgentLength),
            PostedById = FitColumn(identity.UserId, Message.MaxIdentityLength),
            PostedBy = FitColumn(identity.UserName, Message.MaxIdentityLength),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        return MessageDto.From(message);
    }

    public async Task<IReadOnlyList<MessageDto>> ListAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Message> messages = db.Messages.AsNoTracking();

        if (query.Label is { } label)
        {
            messages = messages.Where(m => m.Label == label);
        }

        if (query.EffectiveSinceUtc is { } since)
        {
            messages = messages.Where(m => m.CreatedAtUtc >= since);
        }

        if (query.EffectiveSearch is { } term)
        {
            var pattern = $"%{EscapeLike(term)}%";
            messages = messages.Where(m => EF.Functions.Like(m.Content, pattern, LikeEscape));
        }

        var rows = await messages
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(query.EffectiveLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(MessageDto.From).ToList();
    }

    /// <summary>Blank identity parts are stored as <see cref="AgentIdentity.Unknown"/>; over-long ones are cut to the column size.</summary>
    private static string FitColumn(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? AgentIdentity.Unknown : value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Escapes the escape character first, then the LIKE wildcards, so a search term matches literally.</summary>
    private static string EscapeLike(string term) =>
        term.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
