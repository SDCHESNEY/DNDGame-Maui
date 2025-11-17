#nullable enable
using DNDGame.Services.P2P;

namespace DNDGame.Services.Interfaces;

public interface IP2PTransport : IAsyncDisposable
{
    event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;
    event EventHandler<PeerConnectionEventArgs>? PeerConnected;
    event EventHandler<PeerConnectionEventArgs>? PeerDisconnected;
    event EventHandler<PeerMessageEventArgs>? MessageReceived;
    event EventHandler<PeerSecurityEventArgs>? SecurityAlert;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task ConnectAsync(PeerDescriptor peer, CancellationToken ct = default);
    Task SendAsync(string peerId, ReadOnlyMemory<byte> payload, CancellationToken ct = default);
    Task BroadcastAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);
    IReadOnlyCollection<PeerDescriptor> GetKnownPeers();
}
