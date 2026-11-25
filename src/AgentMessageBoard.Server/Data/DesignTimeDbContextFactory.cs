using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentMessageBoard.Server.Data;

/// <summary>Used only by <c>dotnet ef</c> so migrations can be generated without booting the web host (and its Keycloak configuration).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MessageBoardDbContext>
{
    public MessageBoardDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MessageBoardDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
