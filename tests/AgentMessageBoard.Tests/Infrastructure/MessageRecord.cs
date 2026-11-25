using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMessageBoard.Tests.Infrastructure;

/// <summary>Wire shape of a board message as returned by both the REST endpoint and the MCP tools (camelCase).</summary>
internal sealed record MessageRecord(
    long Id,
    string Label,
    string Content,
    string UserAgent,
    string PostedBy,
    DateTime CreatedAtUtc);

internal static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static MessageRecord ToMessage(this JsonElement element) =>
        element.Deserialize<MessageRecord>(Options) ?? throw new InvalidOperationException("Message JSON deserialized to null.");

    public static IReadOnlyList<MessageRecord> ToMessages(this JsonElement element) =>
        element.Deserialize<List<MessageRecord>>(Options) ?? throw new InvalidOperationException("Message list deserialized to null.");
}
