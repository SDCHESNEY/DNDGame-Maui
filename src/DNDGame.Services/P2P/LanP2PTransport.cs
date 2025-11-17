#nullable enable
using System.Collections.Concurrent;
using DNDGame.Services.Interfaces;
using DNDGame.Services.P2P.Internal;
using Microsoft.Extensions.Logging;

namespace DNDGame.Services.P2P;

public sealed class LanP2PTransport : IP2PTransport
{
    private static readonly ConcurrentDictionary<string, WeakReference<LanP2PTransport>> ActiveTransports = new(StringComparer.Ordinal);

    private readonly ICryptoService _crypto;
    private readonly ILogger<LanP2PTransport> _logger;
    private readonly LanP2PTransportOptions _options;
    private readonly ConcurrentDictionary<string, PeerDescriptor> _peers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanPeerConnection> _connections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private CancellationTokenSource? _cts;

    public LanP2PTransport(ICryptoService crypto,
                           ILogger<LanP2PTransport> logger,
                           LanP2PTransportOptions? options = null)
    {
        _crypto = crypto;
        _logger = logger;
        _options = options ?? new LanP2PTransportOptions();
    }

    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    public event EventHandler<PeerConnectionEventArgs>? PeerConnected;
    public event EventHandler<PeerConnectionEventArgs>? PeerDisconnected;
    public event EventHandler<PeerMessageEventArgs>? MessageReceived;
    public event EventHandler<PeerSecurityEventArgs>? SecurityAlert;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_cts is not null)
        {
            return;
        }

        await _crypto.InitializeAsync(ct).ConfigureAwait(false);
        _cts = new CancellationTokenSource();
        RegisterSelf();
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        foreach (var connection in _connections.Values)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        _connections.Clear();

        ActiveTransports.TryRemove(_crypto.Identity.PeerId, out _);
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _connectLock.Dispose();
    }

    public async Task ConnectAsync(PeerDescriptor peer, CancellationToken ct = default)
    {
        if (peer.PeerId == _crypto.Identity.PeerId)
        {
            throw new InvalidOperationException("Cannot connect to self");
        }

        if (_connections.ContainsKey(peer.PeerId))
        {
            return;
        }

        if (!ActiveTransports.TryGetValue(peer.PeerId, out var weak) || !weak.TryGetTarget(out var remote))
        {
            throw new InvalidOperationException($"Peer {peer.PeerId} is not currently reachable");
        }

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connections.ContainsKey(peer.PeerId))
            {
                return;
            }

            var (localChannel, remoteChannel) = LoopbackFrameChannel.CreatePair();
            var acceptTask = remote.AcceptLoopbackAsync(remoteChannel, BuildSelfDescriptor(), ct);
            var connection = await LanPeerConnection.CreateInitiatorAsync(
                localChannel,
                _crypto,
                _options,
                _logger,
                HandleMessageAsync,
                OnPeerDisconnected,
                OnSecurityEvent,
                peer.Host,
                peer,
                ct).ConfigureAwait(false);

            if (_connections.TryAdd(connection.Descriptor.PeerId, connection))
            {
                NotifyPeerSeen(connection.Descriptor);
                PeerConnected?.Invoke(this, new PeerConnectionEventArgs(connection.Descriptor));
                await acceptTask.ConfigureAwait(false);
            }
            else
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task SendAsync(string peerId, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(peerId, out var connection))
        {
            throw new InvalidOperationException($"No active connection to peer {peerId}");
        }

        await connection.SendAsync(payload, ct).ConfigureAwait(false);
    }

    public async Task BroadcastAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        foreach (var connection in _connections.Values)
        {
            await connection.SendAsync(payload, ct).ConfigureAwait(false);
        }
    }

    public IReadOnlyCollection<PeerDescriptor> GetKnownPeers()
    {
        return _peers.Values
            .Where(p => !string.Equals(p.PeerId, _crypto.Identity.PeerId, StringComparison.Ordinal))
            .OrderBy(p => p.DeviceName, StringComparer.Ordinal)
            .ToArray();
    }

    private void RegisterSelf()
    {
        ActiveTransports[_crypto.Identity.PeerId] = new WeakReference<LanP2PTransport>(this);
        foreach (var entry in ActiveTransports)
        {
            if (!entry.Value.TryGetTarget(out var transport) || transport == this)
            {
                continue;
            }

            var descriptor = transport.BuildSelfDescriptor();
            NotifyPeerSeen(descriptor);
            transport.NotifyPeerSeen(BuildSelfDescriptor());
        }
    }

    private PeerDescriptor BuildSelfDescriptor() => new(
        _crypto.Identity.PeerId,
        _crypto.Identity.DeviceName,
        Convert.ToBase64String(_crypto.IdentityPublicKey.Span),
        Convert.ToBase64String(_crypto.KeyExchangePublicKey.Span),
        "loopback",
        0,
        DateTimeOffset.UtcNow);

    private void NotifyPeerSeen(PeerDescriptor descriptor)
    {
        var updated = descriptor with { LastSeen = DateTimeOffset.UtcNow };
        var isNew = !_peers.ContainsKey(descriptor.PeerId);
        _peers[descriptor.PeerId] = updated;
        PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(updated, isNew));
    }

    private async Task AcceptLoopbackAsync(IFrameChannel channel, PeerDescriptor remoteDescriptor, CancellationToken ct)
    {
        var connection = await LanPeerConnection.CreateResponderAsync(
            channel,
            _crypto,
            _options,
            _logger,
            HandleMessageAsync,
            OnPeerDisconnected,
            OnSecurityEvent,
            remoteDescriptor.Host,
            ct).ConfigureAwait(false);

        NotifyPeerSeen(connection.Descriptor);
        if (_connections.TryAdd(connection.Descriptor.PeerId, connection))
        {
            PeerConnected?.Invoke(this, new PeerConnectionEventArgs(connection.Descriptor));
        }
        else
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task HandleMessageAsync(PeerMessageEventArgs args)
    {
        MessageReceived?.Invoke(this, args);
        return Task.CompletedTask;
    }

    private void OnPeerDisconnected(PeerConnectionEventArgs args)
    {
        if (_connections.TryRemove(args.Peer.PeerId, out _))
        {
            PeerDisconnected?.Invoke(this, args);
        }
    }

    private void OnSecurityEvent(PeerSecurityEventArgs args)
    {
        SecurityAlert?.Invoke(this, args);
    }
}
