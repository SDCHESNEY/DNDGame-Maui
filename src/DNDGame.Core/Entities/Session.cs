#nullable enable
namespace DNDGame.Core.Entities;

public class Session
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
