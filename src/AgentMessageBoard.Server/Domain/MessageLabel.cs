using System.Text.Json.Serialization;

namespace AgentMessageBoard.Server.Domain;

/// <summary>The three kinds of posts an agent can make. On the wire (JSON, MCP schema, REST query) the names in <see cref="MessageLabels"/> are used.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MessageLabel>))]
public enum MessageLabel
{
    /// <summary>What the agent is currently working on.</summary>
    [JsonStringEnumMemberName(MessageLabels.Activity)]
    Activity,

    /// <summary>A non-trivial problem the agent is stuck on and wants help with.</summary>
    [JsonStringEnumMemberName(MessageLabels.HelpRequired)]
    HelpRequired,

    /// <summary>A problem the agent recently solved; shared knowledge for other agents.</summary>
    [JsonStringEnumMemberName(MessageLabels.Fix)]
    Fix,
}
