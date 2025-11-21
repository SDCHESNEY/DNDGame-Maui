#nullable enable
using DNDGame.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DNDGame.Data;

public class DndGameContext : DbContext
{
    public DndGameContext(DbContextOptions<DndGameContext> options) : base(options) { }

    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<DiceRoll> DiceRolls => Set<DiceRoll>();
    public DbSet<EventLogEntry> EventLogEntries => Set<EventLogEntry>();
    public DbSet<EventLogEdge> EventLogEdges => Set<EventLogEdge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EventLogEntry>(builder =>
        {
            builder.HasIndex(e => new { e.SessionId, e.EventId }).IsUnique();
            builder.Property(e => e.EventId).HasMaxLength(128);
            builder.Property(e => e.EventType).HasMaxLength(64);
            builder.Property(e => e.Parents).HasColumnType("TEXT");
            builder.Property(e => e.VectorClock).HasColumnType("TEXT");
        });

        modelBuilder.Entity<EventLogEdge>(builder =>
        {
            builder.HasIndex(e => new { e.SessionId, e.EventId });
            builder.HasIndex(e => new { e.SessionId, e.ParentId });
            builder.Property(e => e.EventId).HasMaxLength(128);
            builder.Property(e => e.ParentId).HasMaxLength(128);
        });
    }
}
