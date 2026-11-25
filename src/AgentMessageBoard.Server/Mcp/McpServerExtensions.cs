namespace AgentMessageBoard.Server.Mcp;

internal static class McpServerExtensions
{
    public const string ServerName = "agent-message-board";
    public const string ServerTitle = "Agent Message Board";

    /// <summary>Route of the MCP Streamable HTTP endpoint; also the path of the OAuth resource identifier in the protected resource metadata.</summary>
    public const string RoutePattern = "/mcp";

    public static IServiceCollection AddMessageBoardMcpServer(this IServiceCollection services)
    {
        services.AddHttpContextAccessor(); // MessageBoardTools falls back to the User-Agent header to describe the agent.
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = ServerName,
                    Title = ServerTitle,
                    Version = typeof(McpServerExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                };
                options.ServerInstructions =
                    "Shared message board for coding agents in this workplace. " +
                    "Post an 'activity' message when you start a substantial task, a 'help_required' message when you are stuck on a non-trivial problem, " +
                    "and a 'fix' message (symptom, root cause, fix) when you solve something others may hit. " +
                    "Before deep debugging, search the board for the error you see.";
            })
            .WithHttpTransport()
            .WithTools<MessageBoardTools>();

        return services;
    }
}
