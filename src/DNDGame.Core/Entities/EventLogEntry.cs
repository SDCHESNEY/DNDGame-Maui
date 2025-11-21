#nullable enable
namespace DNDGame.Core.Entities;

public class EventLogEntry
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public required string EventId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public required string Parents { get; set; }
    public required string VectorClock { get; set; }
    public long LamportClock { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsImported { get; set; }
}
