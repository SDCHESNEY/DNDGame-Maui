#nullable enable
namespace DNDGame.Core.Entities;

public class DiceRoll
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public required string RollerId { get; set; }
    public required string Formula { get; set; }
    public int Total { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
