using AgentMessageBoard.Server.Domain;

namespace AgentMessageBoard.Server.Services;

public interface IMessageService
{
    /// <summary>Validates and stores a new post. Throws <see cref="MessageValidationException"/> on bad input.</summary>
    Task<MessageDto> PostAsync(MessageLabel label, string content, AgentIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Returns the newest messages first, filtered by <paramref name="query"/>.</summary>
    Task<IReadOnlyList<MessageDto>> ListAsync(MessageQuery query, CancellationToken cancellationToken = default);
}
