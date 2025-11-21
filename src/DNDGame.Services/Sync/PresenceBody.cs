#nullable enable
namespace DNDGame.Services.Sync;

public sealed record PresenceBody(
    string PeerId,
    bool IsOnline,
    long Version,
    DateTimeOffset UpdatedAt,
    string DeviceName,
    Guid ChangeId,
    string? Status = null) : ISyncEventBody
{
    public SyncEventKind Kind => SyncEventKind.Presence;
}
