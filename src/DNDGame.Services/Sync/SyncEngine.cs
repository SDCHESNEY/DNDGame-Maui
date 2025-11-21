#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DNDGame.Core.Entities;
using DNDGame.Data;
using DNDGame.Services.Dice;
using DNDGame.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DNDGame.Services.Sync;

public sealed class SyncEngine : ISyncEngine, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DndGameContext _context;
    private readonly ICryptoService _cryptoService;
    private readonly ILogger<SyncEngine> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly ConcurrentDictionary<int, VectorClock> _sessionClocks = new();
    private long _lamportClock;
    private bool _initialized;

    public SyncEngine(DndGameContext context, ICryptoService cryptoService, ILogger<SyncEngine> logger)
    {
        _context = context;
        _cryptoService = cryptoService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _cryptoService.InitializeAsync(ct).ConfigureAwait(false);

            _lamportClock = await _context.EventLogEntries.AsNoTracking()
                .MaxAsync(static e => (long?)e.LamportClock, ct)
                .ConfigureAwait(false) ?? 0;

            var snapshots = await _context.EventLogEntries.AsNoTracking()
                .Select(e => new { e.SessionId, e.VectorClock })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var snapshot in snapshots)
            {
                var clock = VectorClock.FromJson(snapshot.VectorClock);
                _sessionClocks.AddOrUpdate(snapshot.SessionId, clock, (_, existing) => existing.Merge(clock));
            }

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<SyncEventRecord> AppendLocalEventAsync(int sessionId, ISyncEventBody body, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var parents = await GetSessionHeadIdsInternalAsync(sessionId, ct).ConfigureAwait(false);
            var currentClock = _sessionClocks.GetOrAdd(sessionId, VectorClock.Empty);
            var vectorClock = currentClock.Increment(_cryptoService.Identity.PeerId);
            _sessionClocks[sessionId] = vectorClock;
            var lamport = Interlocked.Increment(ref _lamportClock);
            var timestamp = DateTimeOffset.UtcNow;
            var record = await PersistEventAsync(sessionId, body, parents, vectorClock, lamport, timestamp, false, null, ct).ConfigureAwait(false);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<int> ImportAsync(IEnumerable<SyncEventRecord> events, CancellationToken ct = default)
    {
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var ordered = events.OrderBy(static e => e.LamportClock).ThenBy(static e => e.EventId, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0)
        {
            return 0;
        }

        var incomingIds = ordered.Select(static e => e.EventId).ToArray();
        var existingIds = await _context.EventLogEntries.AsNoTracking()
            .Where(e => incomingIds.Contains(e.EventId))
            .Select(static e => e.EventId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existing = new HashSet<string>(existingIds, StringComparer.Ordinal);

        var imported = 0;
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var record in ordered)
            {
                if (existing.Contains(record.EventId))
                {
                    continue;
                }

                await PersistEventAsync(record.SessionId, record.Body, record.Parents, record.VectorClock, record.LamportClock, record.Timestamp, true, record.EventId, ct).ConfigureAwait(false);
                MergeClock(record.SessionId, record.VectorClock);
                _lamportClock = Math.Max(_lamportClock, record.LamportClock);
                imported++;
            }

            if (imported > 0)
            {
                await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }

        return imported;
    }

    public async Task<IReadOnlyList<SyncEventRecord>> GetEventsAsync(int sessionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var entities = await _context.EventLogEntries.AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderBy(static e => e.LamportClock)
            .ThenBy(static e => e.EventId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return MapEntities(entities);
    }

    public async Task<IReadOnlyList<SyncEventRecord>> GetMissingEventsAsync(int sessionId, IEnumerable<string> knownEventIds, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var known = (knownEventIds ?? Array.Empty<string>()).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var query = _context.EventLogEntries.AsNoTracking().Where(e => e.SessionId == sessionId);
        if (known.Length > 0)
        {
            query = query.Where(e => !known.Contains(e.EventId));
        }

        var entities = await query
            .OrderBy(static e => e.LamportClock)
            .ThenBy(static e => e.EventId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return MapEntities(entities);
    }

    public async Task<IReadOnlyList<string>> GetHeadEventIdsAsync(int sessionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await GetSessionHeadIdsInternalAsync(sessionId, ct).ConfigureAwait(false);
    }

    public async Task<SyncMaterializedState> GetSessionStateAsync(int sessionId, CancellationToken ct = default)
    {
        var events = await GetEventsAsync(sessionId, ct).ConfigureAwait(false);
        var ordered = OrderEvents(events);
        return MaterializeState(ordered);
    }

    public ValueTask DisposeAsync()
    {
        _writeGate.Dispose();
        _initGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await InitializeAsync(ct).ConfigureAwait(false);
    }

    private async Task<SyncEventRecord> PersistEventAsync(
        int sessionId,
        ISyncEventBody body,
        IReadOnlyList<string> parents,
        VectorClock vectorClock,
        long lamport,
        DateTimeOffset timestamp,
        bool isImported,
        string? knownEventId,
        CancellationToken ct)
    {
        var payload = SyncEventSerializer.Serialize(body);
        var eventId = ComputeEventId(sessionId, body.Kind, lamport, timestamp, vectorClock, parents, payload);
        if (knownEventId is not null && !string.Equals(eventId, knownEventId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Content hash mismatch for event {knownEventId}. Expected {eventId}.");
        }

        var entity = new EventLogEntry
        {
            SessionId = sessionId,
            EventId = knownEventId ?? eventId,
            EventType = body.Kind.ToString(),
            Payload = payload,
            Parents = SerializeParents(parents),
            VectorClock = vectorClock.ToJson(),
            LamportClock = lamport,
            CreatedAt = timestamp,
            IsImported = isImported
        };

        await _context.EventLogEntries.AddAsync(entity, ct).ConfigureAwait(false);
        foreach (var parent in parents)
        {
            var edge = new EventLogEdge
            {
                SessionId = sessionId,
                EventId = entity.EventId,
                ParentId = parent
            };

            await _context.EventLogEdges.AddAsync(edge, ct).ConfigureAwait(false);
        }

        return new SyncEventRecord(entity.EventId, sessionId, body.Kind, lamport, timestamp, parents, vectorClock, body, isImported);
    }

    private static string ComputeEventId(int sessionId, SyncEventKind kind, long lamport, DateTimeOffset timestamp, VectorClock vectorClock, IReadOnlyList<string> parents, string payload)
    {
        var builder = new StringBuilder();
        builder.Append(sessionId)
            .Append('|').Append((int)kind)
            .Append('|').Append(lamport)
            .Append('|').Append(timestamp.ToUnixTimeMilliseconds())
            .Append('|').Append(vectorClock.ToDeterministicString());

        foreach (var parent in parents.OrderBy(static p => p, StringComparer.Ordinal))
        {
            builder.Append('|').Append(parent);
        }

        builder.Append('|').Append(payload);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private async Task<IReadOnlyList<string>> GetSessionHeadIdsInternalAsync(int sessionId, CancellationToken ct)
    {
        var query = from entry in _context.EventLogEntries.AsNoTracking().Where(e => e.SessionId == sessionId)
                    join edge in _context.EventLogEdges.AsNoTracking().Where(e => e.SessionId == sessionId)
                        on entry.EventId equals edge.ParentId into edgeGroup
                    where !edgeGroup.Any()
                    select entry.EventId;

        var heads = await query.OrderBy(static id => id).ToListAsync(ct).ConfigureAwait(false);
        return heads.Count == 0 ? Array.Empty<string>() : heads;
    }

    private static string SerializeParents(IReadOnlyList<string> parents)
        => JsonSerializer.Serialize(parents, SerializerOptions);

    private static IReadOnlyList<string> DeserializeParents(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        var payload = JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? new List<string>();
        return new ReadOnlyCollection<string>(payload);
    }

    private IReadOnlyList<SyncEventRecord> MapEntities(IReadOnlyList<EventLogEntry> entities)
    {
        var events = new List<SyncEventRecord>(entities.Count);
        foreach (var entity in entities)
        {
            if (!Enum.TryParse<SyncEventKind>(entity.EventType, ignoreCase: true, out var kind))
            {
                _logger.LogWarning("Skipping event {EventId} with unknown type {EventType}", entity.EventId, entity.EventType);
                continue;
            }

            try
            {
                var body = SyncEventSerializer.Deserialize(kind, entity.Payload);
                var parents = DeserializeParents(entity.Parents);
                var vectorClock = VectorClock.FromJson(entity.VectorClock);
                events.Add(new SyncEventRecord(entity.EventId, entity.SessionId, kind, entity.LamportClock, entity.CreatedAt, parents, vectorClock, body, entity.IsImported));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize event {EventId}", entity.EventId);
            }
        }

        return events;
    }

    private static IReadOnlyList<SyncEventRecord> OrderEvents(IReadOnlyList<SyncEventRecord> events)
    {
        if (events.Count <= 1)
        {
            return events;
        }

        var map = events.ToDictionary(static e => e.EventId, StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var evt in events)
        {
            var count = 0;
            foreach (var parent in evt.Parents)
            {
                if (!map.ContainsKey(parent))
                {
                    continue;
                }

                if (!adjacency.TryGetValue(parent, out var list))
                {
                    list = new List<string>();
                    adjacency[parent] = list;
                }

                list.Add(evt.EventId);
                count++;
            }

            indegree[evt.EventId] = count;
        }

        var ready = new PriorityQueue<SyncEventRecord, (long Lamport, string EventId)>();
        foreach (var evt in events)
        {
            if (!indegree.TryGetValue(evt.EventId, out var value) || value == 0)
            {
                ready.Enqueue(evt, (evt.LamportClock, evt.EventId));
            }
        }

        var ordered = new List<SyncEventRecord>(events.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (ready.TryDequeue(out var next, out _))
        {
            if (!seen.Add(next.EventId))
            {
                continue;
            }

            ordered.Add(next);
            if (!adjacency.TryGetValue(next.EventId, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (!indegree.ContainsKey(childId))
                {
                    continue;
                }

                indegree[childId]--;
                if (indegree[childId] == 0 && map.TryGetValue(childId, out var child))
                {
                    ready.Enqueue(child, (child.LamportClock, child.EventId));
                }
            }
        }

        if (ordered.Count != events.Count)
        {
            foreach (var evt in events.OrderBy(static e => e.LamportClock).ThenBy(static e => e.EventId, StringComparer.Ordinal))
            {
                if (seen.Add(evt.EventId))
                {
                    ordered.Add(evt);
                }
            }
        }

        return ordered;
    }

    private SyncMaterializedState MaterializeState(IReadOnlyList<SyncEventRecord> events)
    {
        var chatById = new Dictionary<Guid, ChatMessageState>();
        var chatOrdered = new List<ChatMessageState>();
        var presence = new Dictionary<string, PresenceState>(StringComparer.Ordinal);
        var flags = new Dictionary<string, FlagState>(StringComparer.Ordinal);
        var dice = new List<DiceRollState>();

        foreach (var evt in events)
        {
            switch (evt.Kind)
            {
                case SyncEventKind.ChatMessage:
                    ApplyChat((ChatMessageBody)evt.Body, evt.EventId, chatById, chatOrdered);
                    break;
                case SyncEventKind.Presence:
                    ApplyPresence((PresenceBody)evt.Body, evt.EventId, presence);
                    break;
                case SyncEventKind.FlagUpdate:
                    ApplyFlag((FlagUpdateBody)evt.Body, evt.EventId, flags);
                    break;
                case SyncEventKind.DiceRoll:
                    ApplyDice((DiceRollBody)evt.Body, evt.EventId, dice);
                    break;
            }
        }

        return new SyncMaterializedState(
            chatOrdered,
            new ReadOnlyDictionary<string, PresenceState>(presence),
            new ReadOnlyDictionary<string, FlagState>(flags),
            new ReadOnlyCollection<DiceRollState>(dice));
    }

    private static void ApplyChat(ChatMessageBody body, string eventId, IDictionary<Guid, ChatMessageState> chatById, IList<ChatMessageState> ordered)
    {
        if (chatById.ContainsKey(body.MessageId))
        {
            return;
        }

        var state = new ChatMessageState(body.MessageId, body.PeerId, body.DeviceName, body.Content, body.CreatedAt, eventId);
        var insertIndex = FindChatIndex(ordered, body.AfterEventId);
        if (insertIndex >= 0)
        {
            ordered.Insert(Math.Min(insertIndex + 1, ordered.Count), state);
        }
        else
        {
            ordered.Add(state);
        }

        chatById[body.MessageId] = state;
    }

    private static int FindChatIndex(IList<ChatMessageState> ordered, string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return -1;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].EventId == eventId)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyPresence(PresenceBody body, string eventId, IDictionary<string, PresenceState> presence)
    {
        if (presence.TryGetValue(body.PeerId, out var existing))
        {
            if (!ShouldReplace(existing.Version, existing.UpdatedAt, existing.EventId, body.Version, body.UpdatedAt, eventId))
            {
                return;
            }
        }

        presence[body.PeerId] = new PresenceState(body.PeerId, body.IsOnline, body.Version, body.UpdatedAt, body.DeviceName, body.Status, eventId);
    }

    private static void ApplyFlag(FlagUpdateBody body, string eventId, IDictionary<string, FlagState> flags)
    {
        if (flags.TryGetValue(body.Key, out var existing))
        {
            if (!ShouldReplace(existing.Version, existing.UpdatedAt, existing.EventId, body.Version, body.UpdatedAt, eventId))
            {
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(body.Value))
        {
            flags.Remove(body.Key);
            return;
        }

        flags[body.Key] = new FlagState(body.Key, body.Value, body.Version, body.UpdatedAt, eventId);
    }

    private void ApplyDice(DiceRollBody body, string eventId, IList<DiceRollState> dice)
    {
        var signatureValid = VerifyDiceSignature(body);
        dice.Add(new DiceRollState(
            body.Evidence.RollId,
            body.Evidence.RollerPeerId,
            body.Evidence.RollerDeviceName,
            body.Evidence.Formula,
            body.Evidence.Mode,
            body.Evidence.Modifier,
            body.Evidence.Total,
            body.Evidence.Components,
            signatureValid,
            body.Evidence.Timestamp,
            eventId));
    }

    private bool VerifyDiceSignature(DiceRollBody body)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(body.Evidence, SerializerOptions);
            var signature = Convert.FromBase64String(body.Signature);
            var identityKey = Convert.FromBase64String(body.Evidence.IdentityPublicKey);
            return _cryptoService.Verify(payload, signature, identityKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dice signature verification failed for {RollId}", body.Evidence.RollId);
            return false;
        }
    }

    private static bool ShouldReplace(long existingVersion, DateTimeOffset existingTimestamp, string existingEventId, long candidateVersion, DateTimeOffset candidateTimestamp, string candidateEventId)
    {
        if (candidateVersion != existingVersion)
        {
            return candidateVersion > existingVersion;
        }

        if (candidateTimestamp != existingTimestamp)
        {
            return candidateTimestamp > existingTimestamp;
        }

        return string.Compare(candidateEventId, existingEventId, StringComparison.Ordinal) > 0;
    }

    private void MergeClock(int sessionId, VectorClock clock)
        => _sessionClocks.AddOrUpdate(sessionId, clock, (_, existing) => existing.Merge(clock));
}
