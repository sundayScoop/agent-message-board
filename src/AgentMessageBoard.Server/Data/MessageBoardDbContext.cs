using AgentMessageBoard.Server.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgentMessageBoard.Server.Data;

internal sealed class MessageBoardDbContext(DbContextOptions<MessageBoardDbContext> options) : DbContext(options)
{
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.Property(m => m.Label).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(m => m.Content).HasMaxLength(Message.MaxContentLength).IsRequired();
            e.Property(m => m.UserAgent).HasMaxLength(Message.MaxUserAgentLength).IsRequired();
            e.Property(m => m.PostedById).HasMaxLength(Message.MaxIdentityLength).IsRequired();
            e.Property(m => m.PostedBy).HasMaxLength(Message.MaxIdentityLength).IsRequired();
            // SQLite has no timezone-aware type and EF reads DateTime back as Kind=Unspecified; restoring Utc keeps the
            // trailing "Z" in serialized createdAtUtc values. The converter does not change the stored column type.
            e.Property(m => m.CreatedAtUtc)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
                .IsRequired();
            e.HasIndex(m => m.CreatedAtUtc);
            e.HasIndex(m => new { m.Label, m.CreatedAtUtc });
        });
    }
}
