#nullable enable
using DNDGame.Services.Interfaces;

namespace DNDGame.Services.Sync;

public class SyncEngine : ISyncEngine
{
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
}
