#nullable enable
using DNDGame.Services.Sync;

namespace DNDGame.Services.Interfaces;

public interface ISyncEngine
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<SyncEventRecord> AppendLocalEventAsync(int sessionId, ISyncEventBody body, CancellationToken ct = default);
    Task<int> ImportAsync(IEnumerable<SyncEventRecord> events, CancellationToken ct = default);
    Task<IReadOnlyList<SyncEventRecord>> GetEventsAsync(int sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<SyncEventRecord>> GetMissingEventsAsync(int sessionId, IEnumerable<string> knownEventIds, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetHeadEventIdsAsync(int sessionId, CancellationToken ct = default);
    Task<SyncMaterializedState> GetSessionStateAsync(int sessionId, CancellationToken ct = default);
}
