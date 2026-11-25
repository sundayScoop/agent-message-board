using AgentMessageBoard.Server.Auth;
using AgentMessageBoard.Server.Domain;
using AgentMessageBoard.Server.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgentMessageBoard.Server.Endpoints;

/// <summary>The single human-facing read endpoint: the latest messages as JSON. Bearer token required, like everything else.</summary>
internal static class MessagesEndpoints
{
    public static IEndpointRouteBuilder MapMessagesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/messages", GetLatestMessages)
            .WithName("GetLatestMessages")
            .RequireAuthorization(Policies.AuthenticatedAgent);

        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<MessageDto>>, ValidationProblem>> GetLatestMessages(
        IMessageService messages,
        CancellationToken cancellationToken,
        string? label = null,
        int? limit = null,
        DateTime? since = null,
        string? q = null)
    {
        MessageLabel? labelFilter = null;
        if (!string.IsNullOrWhiteSpace(label))
        {
            if (!MessageLabels.TryParse(label, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["label"] = [$"label must be one of: {MessageLabels.All}."],
                });
            }

            labelFilter = parsed;
        }

        var result = await messages.ListAsync(new MessageQuery(labelFilter, limit, since, q), cancellationToken);
        return TypedResults.Ok(result);
    }
}
