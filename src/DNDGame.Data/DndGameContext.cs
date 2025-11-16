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
}
