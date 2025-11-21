#nullable enable
namespace DNDGame.Core.Entities;

public class EventLogEdge
{
    public int Id { get; set; }
    public required string EventId { get; set; }
    public required string ParentId { get; set; }
    public int SessionId { get; set; }
}
