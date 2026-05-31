using ClaudeEnterprise.Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaudeEnterprise.Infrastructure.Persistence;

public sealed class ChatDbContext : DbContext
{
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetToTicks =
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var conv = modelBuilder.Entity<ConversationEntity>();
        conv.ToTable("Conversations");
        conv.HasKey(x => x.Id);
        conv.Property(x => x.Title).HasMaxLength(256).IsRequired();
        conv.Property(x => x.CreatedAt).HasConversion(DateTimeOffsetToTicks).IsRequired();
        conv.Property(x => x.UpdatedAt).HasConversion(DateTimeOffsetToTicks).IsRequired();
        conv.Property(x => x.ETag).HasMaxLength(64).IsRequired();
        conv.Property(x => x.IsDeleted).HasDefaultValue(false);
        conv.HasIndex(x => x.UpdatedAt);
        conv.HasIndex(x => x.IsDeleted);
        conv.HasQueryFilter(x => !x.IsDeleted);

        var msg = modelBuilder.Entity<MessageEntity>();
        msg.ToTable("Messages");
        msg.HasKey(x => x.Id);
        msg.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        msg.Property(x => x.Content).IsRequired();
        msg.Property(x => x.CreatedAt).HasConversion(DateTimeOffsetToTicks).IsRequired();
        msg.Property(x => x.Position).IsRequired();
        msg.HasIndex(x => new { x.ConversationId, x.Position });
        msg.HasOne<ConversationEntity>()
            .WithMany(c => c.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConversationEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ETag { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public List<MessageEntity> Messages { get; set; } = new();
}

public sealed class MessageEntity
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public int Position { get; set; }
    public ChatRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
