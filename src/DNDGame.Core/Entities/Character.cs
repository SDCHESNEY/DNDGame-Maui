#nullable enable
namespace DNDGame.Core.Entities;

public class Character
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; } = 1;
}
