#nullable enable
namespace DNDGame.Services.Sync;

public interface ISyncEventBody
{
    SyncEventKind Kind { get; }
}
