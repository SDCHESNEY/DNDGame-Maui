#nullable enable
namespace DNDGame.Services.P2P;

public sealed class LanP2PTransportOptions
{
    public TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan DiscoveryBroadcastInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan PeerExpiry { get; set; } = TimeSpan.FromSeconds(20);
    public bool UseLoopbackBus { get; set; } = true;
}
