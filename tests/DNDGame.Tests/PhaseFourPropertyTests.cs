#nullable enable
using System.Globalization;
using DNDGame.Data;
using DNDGame.Services.Crypto;
using DNDGame.Services.Sync;
using DNDGame.Tests.Fakes;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DNDGame.Tests;

public sealed class PhaseFourPropertyTests
{
    private const int SessionId = 1;

    [Property(MaxTest = 25)]
    public bool SyncEnginesReachEquivalentState(int[] peerASeed, int[] peerBSeed)
    {
        var peerAOps = SyncOperationFactory.BuildOperations(peerASeed);
        var peerBOps = SyncOperationFactory.BuildOperations(peerBSeed);
        return RunConvergenceAsync(peerAOps, peerBOps).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 30)]
    public bool MissingEventsExcludeKnownIds(int[] operationSeed, NonNegativeInt salt)
    {
        var operations = SyncOperationFactory.BuildOperations(operationSeed);
        return RunMissingEventsPropertyAsync(operations, salt.Get).GetAwaiter().GetResult();
    }

    private static async Task<bool> RunConvergenceAsync(IReadOnlyCollection<SyncOperation> peerAOps, IReadOnlyCollection<SyncOperation> peerBOps)
    {
        await using var harnessA = await SyncEngineHarness.CreateAsync().ConfigureAwait(false);
        await using var harnessB = await SyncEngineHarness.CreateAsync().ConfigureAwait(false);

        await harnessA.ExecuteAsync(peerAOps, SessionId).ConfigureAwait(false);
        await harnessB.ExecuteAsync(peerBOps, SessionId).ConfigureAwait(false);

        var eventsA = await harnessA.Engine.GetEventsAsync(SessionId).ConfigureAwait(false);
        var eventsB = await harnessB.Engine.GetEventsAsync(SessionId).ConfigureAwait(false);

        await harnessA.Engine.ImportAsync(eventsB).ConfigureAwait(false);
        await harnessB.Engine.ImportAsync(eventsA).ConfigureAwait(false);

        var stateA = await harnessA.Engine.GetSessionStateAsync(SessionId).ConfigureAwait(false);
        var stateB = await harnessB.Engine.GetSessionStateAsync(SessionId).ConfigureAwait(false);

        return AreStatesEquivalent(stateA, stateB);
    }

    private static async Task<bool> RunMissingEventsPropertyAsync(IReadOnlyCollection<SyncOperation> operations, int salt)
    {
        await using var harness = await SyncEngineHarness.CreateAsync().ConfigureAwait(false);
        await harness.ExecuteAsync(operations, SessionId).ConfigureAwait(false);

        var events = await harness.Engine.GetEventsAsync(SessionId).ConfigureAwait(false);
        var known = SelectKnownIds(events, salt);
        var missing = await harness.Engine.GetMissingEventsAsync(SessionId, known).ConfigureAwait(false);

        var expected = events
            .Where(e => !known.Contains(e.EventId, StringComparer.Ordinal))
            .Select(e => e.EventId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var actual = missing
            .Select(e => e.EventId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return expected.SequenceEqual(actual);
    }

    private static IReadOnlyCollection<string> SelectKnownIds(IReadOnlyList<SyncEventRecord> events, int salt)
    {
        if (events.Count == 0)
        {
            return Array.Empty<string>();
        }

        var rng = new Random(HashCode.Combine(salt, events.Count));
        var known = new List<string>();
        foreach (var evt in events)
        {
            if (rng.NextDouble() < 0.5)
            {
                known.Add(evt.EventId);
            }
        }

        var extras = rng.Next(0, 3);
        for (var i = 0; i < extras; i++)
        {
            known.Add(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        }

        return known;
    }

    private static bool AreStatesEquivalent(SyncMaterializedState left, SyncMaterializedState right)
    {
        var chatEqual = left.Chat
            .Select(static c => (c.MessageId, c.PeerId, c.Content))
            .SequenceEqual(right.Chat.Select(static c => (c.MessageId, c.PeerId, c.Content)));

        var flagsEqual = left.Flags
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .Select(static kvp => (kvp.Key, kvp.Value.Value, kvp.Value.Version))
            .SequenceEqual(right.Flags
                .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
                .Select(static kvp => (kvp.Key, kvp.Value.Value, kvp.Value.Version)));

        var presenceEqual = left.Presence
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .Select(static kvp => (kvp.Key, kvp.Value.IsOnline, kvp.Value.Version))
            .SequenceEqual(right.Presence
                .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
                .Select(static kvp => (kvp.Key, kvp.Value.IsOnline, kvp.Value.Version)));

        var diceEqual = left.DiceHistory
            .Select(static d => (d.RollId, d.Total, d.SignatureValid))
            .SequenceEqual(right.DiceHistory.Select(static d => (d.RollId, d.Total, d.SignatureValid)));

        return chatEqual && flagsEqual && presenceEqual && diceEqual;
    }
}

file sealed class SyncEngineHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, long> _flagVersions = new(StringComparer.Ordinal);

    private SyncEngineHarness(SqliteConnection connection, DndGameContext context, CryptoService crypto, SyncEngine engine)
    {
        _connection = connection;
        Context = context;
        Crypto = crypto;
        Engine = engine;
    }

    public DndGameContext Context { get; }
    public CryptoService Crypto { get; }
    public SyncEngine Engine { get; }

    public static async Task<SyncEngineHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        var options = new DbContextOptionsBuilder<DndGameContext>()
            .UseSqlite(connection)
            .Options;
        var context = new DndGameContext(options);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        var storage = new InMemorySecureStorageProvider();
        var crypto = new CryptoService(storage, NullLogger<CryptoService>.Instance);
        await crypto.InitializeAsync().ConfigureAwait(false);

        var engine = new SyncEngine(context, crypto, NullLogger<SyncEngine>.Instance);
        await engine.InitializeAsync().ConfigureAwait(false);

        return new SyncEngineHarness(connection, context, crypto, engine);
    }

    public async Task ExecuteAsync(IEnumerable<SyncOperation> operations, int sessionId)
    {
        foreach (var operation in operations ?? Array.Empty<SyncOperation>())
        {
            await ExecuteAsync(operation, sessionId).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(SyncOperation operation, int sessionId)
    {
        switch (operation.Kind)
        {
            case SyncOperationKind.Chat:
                var chatBody = new ChatMessageBody(Guid.NewGuid(), Crypto.Identity.PeerId, Crypto.Identity.DeviceName, operation.Key, DateTimeOffset.UtcNow);
                await Engine.AppendLocalEventAsync(sessionId, chatBody).ConfigureAwait(false);
                break;
            case SyncOperationKind.FlagSet:
                await AppendFlagAsync(operation.Key, operation.Value ?? operation.Key, sessionId).ConfigureAwait(false);
                break;
            case SyncOperationKind.FlagClear:
                await AppendFlagAsync(operation.Key, null, sessionId).ConfigureAwait(false);
                break;
        }
    }

    private async Task AppendFlagAsync(string key, string? value, int sessionId)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "flag" : key;
        var version = _flagVersions.TryGetValue(normalized, out var current) ? current + 1 : 1;
        _flagVersions[normalized] = version;

        var body = new FlagUpdateBody(normalized, value, version, DateTimeOffset.UtcNow, Guid.NewGuid());
        await Engine.AppendLocalEventAsync(sessionId, body).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync().ConfigureAwait(false);
        await Context.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}

public enum SyncOperationKind
{
    Chat,
    FlagSet,
    FlagClear
}

public sealed record SyncOperation(SyncOperationKind Kind, string Key, string? Value);

internal static class SyncOperationFactory
{
    private static readonly string[] TokenPool =
    [
        "alpha", "beta", "gamma", "delta", "omega", "sigma", "tau", "kappa", "zephyr", "ember"
    ];

    private static readonly SyncOperationKind[] AllKinds = Enum.GetValues<SyncOperationKind>();

    public static IReadOnlyList<SyncOperation> BuildOperations(int[]? seed)
    {
        if (seed is null || seed.Length == 0)
        {
            return Array.Empty<SyncOperation>();
        }

        var operations = new List<SyncOperation>(Math.Max(1, seed.Length / 3));
        for (var i = 0; i < seed.Length; i += 3)
        {
            var kindIndex = NormalizeIndex(seed[i], AllKinds.Length);
            var keyIndex = GetTokenIndex(seed, i + 1);
            var valueIndex = GetTokenIndex(seed, i + 2);

            var kind = AllKinds[kindIndex];
            var key = TokenPool[keyIndex];
            var value = TokenPool[valueIndex];

            var operation = kind switch
            {
                SyncOperationKind.Chat => new SyncOperation(kind, $"{key}-{value}", null),
                SyncOperationKind.FlagSet => new SyncOperation(kind, key, value),
                SyncOperationKind.FlagClear => new SyncOperation(kind, key, null),
                _ => new SyncOperation(SyncOperationKind.Chat, key, null)
            };

            operations.Add(operation);
        }

        return operations;
    }

    private static int GetTokenIndex(int[] seed, int position)
    {
        if (seed.Length == 0)
        {
            return 0;
        }

        var sourceIndex = position < seed.Length ? position : position % seed.Length;
        var raw = seed[sourceIndex];
        return NormalizeIndex(raw, TokenPool.Length);
    }

    private static int NormalizeIndex(int raw, int modulo)
    {
        if (modulo <= 0)
        {
            return 0;
        }

        var positive = Math.Abs((long)raw);
        return (int)(positive % modulo);
    }
}
