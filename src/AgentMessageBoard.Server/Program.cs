using AgentMessageBoard.Server.Auth;
using AgentMessageBoard.Server.Data;
using AgentMessageBoard.Server.Endpoints;
using AgentMessageBoard.Server.Mcp;
using AgentMessageBoard.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMessageBoardPersistence(builder.Configuration);
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddMessageBoardAuthentication(builder.Configuration);
builder.Services.AddMessageBoardMcpServer();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync(app.Lifetime.ApplicationStopping);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapMcp(McpServerExtensions.RoutePattern).RequireAuthorization(Policies.AuthenticatedAgent);
app.MapMessagesEndpoints();

app.Run();

/// <summary>Exposed so the test project can host the app with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
