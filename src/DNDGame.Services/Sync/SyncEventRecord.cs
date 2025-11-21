#nullable enable
namespace DNDGame.Services.Sync;

public sealed record SyncEventRecord(
    string EventId,
    int SessionId,
    SyncEventKind Kind,
    long LamportClock,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> Parents,
    VectorClock VectorClock,
    ISyncEventBody Body,
    bool IsImported);
