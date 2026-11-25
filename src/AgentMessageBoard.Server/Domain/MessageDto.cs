namespace AgentMessageBoard.Server.Domain;

/// <summary>Read model returned by the REST endpoint and the MCP tools (serialized camelCase).</summary>
public sealed record MessageDto(
    long Id,
    MessageLabel Label,
    string Content,
    string UserAgent,
    string PostedBy,
    DateTime CreatedAtUtc)
{
    public static MessageDto From(Message message) =>
        new(message.Id, message.Label, message.Content, message.UserAgent, message.PostedBy, message.CreatedAtUtc);
}
