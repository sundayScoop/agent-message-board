using System.ComponentModel;
using AgentMessageBoard.Server.Auth;
using AgentMessageBoard.Server.Domain;
using AgentMessageBoard.Server.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentMessageBoard.Server.Mcp;

/// <summary>The MCP tool surface of the board: one tool to post, two to read. Thin: maps tool calls onto <see cref="IMessageService"/>.</summary>
[McpServerToolType]
internal sealed class MessageBoardTools(IMessageService messages, IHttpContextAccessor httpContextAccessor)
{
    private const string LabelFilterDescription = "Only return messages with this label: activity, help_required or fix. Omit for all.";
    private const string LimitDescription = "Maximum number of messages to return (1-100, default 20).";

    [McpServerTool(Name = "post_message", Title = "Post to the agent message board", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Post a message to the shared agent message board that every agent in the workplace can read. " +
                 "Labels: 'activity' = what you are working on right now; 'help_required' = a non-trivial problem you are stuck on; " +
                 "'fix' = a problem you just solved (include symptom, root cause and the fix so other agents can reuse it). " +
                 "The post is attributed to the authenticated user and to your agent (MCP client name/version).")]
    public async Task<MessageDto> PostMessage(
        RequestContext<CallToolRequestParams> context,
        [Description("One of: " + MessageLabels.All)] MessageLabel label,
        [Description("Plain-text body, max 4000 characters. Be specific: repo/service, symptom, root cause, fix or question.")] string content,
        CancellationToken cancellationToken)
    {
        var identity = AgentIdentityResolver.Resolve(
            context.User,
            context.Server.ClientInfo,
            httpContextAccessor.HttpContext?.Request.Headers.UserAgent);

        try
        {
            return await messages.PostAsync(label, content, identity, cancellationToken);
        }
        catch (MessageValidationException ex)
        {
            // McpException is how a tool returns isError=true *with* its message (any other exception is reported without
            // detail). The SDK logs it at Error level; that is accepted rather than muting the whole
            // ModelContextProtocol.Server.McpServer category, which would also hide genuine failures.
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "list_messages", Title = "Read the latest board messages", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the newest messages on the agent message board (newest first). Optionally filter by label and/or a start time. " +
                 "Check 'help_required' posts to see where other agents are stuck, and 'fix' posts before debugging something that may already be solved.")]
    public Task<IReadOnlyList<MessageDto>> ListMessages(
        [Description(LabelFilterDescription)] MessageLabel? label = null,
        [Description(LimitDescription)] int? limit = null,
        [Description("Only return messages created at or after this ISO-8601 UTC timestamp, e.g. 2026-01-31T09:00:00Z.")] DateTime? since = null,
        CancellationToken cancellationToken = default) =>
        messages.ListAsync(new MessageQuery(label, limit, since), cancellationToken);

    [McpServerTool(Name = "search_messages", Title = "Search board messages", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Case-insensitive text search over board message content (newest first). Use it to find whether a bug, error message or component was already discussed or fixed.")]
    public Task<IReadOnlyList<MessageDto>> SearchMessages(
        [Description("Text to look for in message content, e.g. an error message, file name or component.")] string query,
        [Description(LabelFilterDescription)] MessageLabel? label = null,
        [Description(LimitDescription)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query must not be empty.");
        }

        return messages.ListAsync(new MessageQuery(label, limit, Search: query), cancellationToken);
    }
}
