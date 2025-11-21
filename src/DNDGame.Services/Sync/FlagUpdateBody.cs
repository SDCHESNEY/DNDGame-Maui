#nullable enable
namespace DNDGame.Services.Sync;

public sealed record FlagUpdateBody(
    string Key,
    string? Value,
    long Version,
    DateTimeOffset UpdatedAt,
    Guid ChangeId) : ISyncEventBody
{
    public SyncEventKind Kind => SyncEventKind.FlagUpdate;
}
