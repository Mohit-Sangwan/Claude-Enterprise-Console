using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaudeEnterprise.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef migrations add &lt;Name&gt; -p src/ClaudeEnterprise.Infrastructure -s src/ClaudeEnterprise.Api</c>
/// without booting the full host. Uses a local SQLite file by default.
/// </summary>
public sealed class DesignTimeChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("Persistence__ConnectionString")
                 ?? "Data Source=claude-enterprise.design.db";
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(cs)
            .Options;
        return new ChatDbContext(options);
    }
}
