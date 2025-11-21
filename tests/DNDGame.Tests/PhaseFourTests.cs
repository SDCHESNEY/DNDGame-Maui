#nullable enable
using DNDGame.Data;
using DNDGame.Services.Crypto;
using DNDGame.Services.Dice;
using DNDGame.Services.Sync;
using DNDGame.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DNDGame.Tests;

public sealed class PhaseFourTests : IAsyncLifetime
{
    private SqliteConnection? _connection;
    private DndGameContext? _context;
    private CryptoService? _crypto;
    private SyncEngine? _syncEngine;
    private DiceService? _diceService;

    private SyncEngine Sync => _syncEngine ?? throw new InvalidOperationException("Sync engine not initialized");
    private DiceService Dice => _diceService ?? throw new InvalidOperationException("Dice service not initialized");

    [Fact]
    public async Task AppendLocalEvent_UpdatesHeads()
    {
        var record = await Sync.AppendLocalEventAsync(1, new ChatMessageBody(Guid.NewGuid(), "peer", "device", "hello", DateTimeOffset.UtcNow));
        var heads = await Sync.GetHeadEventIdsAsync(1);
        heads.Should().ContainSingle().Which.Should().Be(record.EventId);
    }

    [Fact]
    public async Task PresenceEvents_LastWriterWins()
    {
        var peerId = "peer-A";
        await Sync.AppendLocalEventAsync(5, new PresenceBody(peerId, true, 1, DateTimeOffset.UtcNow, "device", Guid.NewGuid()));
        await Sync.AppendLocalEventAsync(5, new PresenceBody(peerId, false, 2, DateTimeOffset.UtcNow.AddSeconds(1), "device", Guid.NewGuid()));

        var state = await Sync.GetSessionStateAsync(5);
        state.Presence.Should().ContainKey(peerId);
        state.Presence[peerId].IsOnline.Should().BeFalse();
    }

    [Fact]
    public async Task DiceRoll_SignatureIsVerified()
    {
        await Dice.RollAsync(7, "1d20+3");
        var state = await Sync.GetSessionStateAsync(7);
        state.DiceHistory.Should().ContainSingle().Which.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task ChatEvents_RespectAfterEventOrdering()
    {
        var root = await Sync.AppendLocalEventAsync(3, new ChatMessageBody(Guid.NewGuid(), "peer", "device", "first", DateTimeOffset.UtcNow));
        var second = await Sync.AppendLocalEventAsync(3, new ChatMessageBody(Guid.NewGuid(), "peer", "device", "second", DateTimeOffset.UtcNow.AddMilliseconds(1), root.EventId));
        await Sync.AppendLocalEventAsync(3, new ChatMessageBody(Guid.NewGuid(), "peer", "device", "third", DateTimeOffset.UtcNow.AddMilliseconds(2), second.EventId));

        var state = await Sync.GetSessionStateAsync(3);
        state.Chat.Select(m => m.Content).Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task MissingEvents_ReturnsUnknownEntries()
    {
        var @event = await Sync.AppendLocalEventAsync(10, new FlagUpdateBody("world", "alpha", 1, DateTimeOffset.UtcNow, Guid.NewGuid()));
        var missing = await Sync.GetMissingEventsAsync(10, new[] { "not-present" });
        missing.Should().ContainSingle().Which.EventId.Should().Be(@event.EventId);
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DndGameContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new DndGameContext(options);
        await _context.Database.EnsureCreatedAsync();

        var storage = new InMemorySecureStorageProvider();
        _crypto = new CryptoService(storage, NullLogger<CryptoService>.Instance);
        await _crypto.InitializeAsync();

        _syncEngine = new SyncEngine(_context, _crypto, NullLogger<SyncEngine>.Instance);
        await _syncEngine.InitializeAsync();
        _diceService = new DiceService(_crypto, _syncEngine, NullLogger<DiceService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_syncEngine is not null)
        {
            await _syncEngine.DisposeAsync();
        }

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
     }
 }
