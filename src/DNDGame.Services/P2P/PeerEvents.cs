#nullable enable
namespace DNDGame.Services.P2P;

public sealed class PeerDiscoveredEventArgs : EventArgs
{
    public PeerDescriptor Peer { get; }
    public bool IsNew { get; }

    public PeerDiscoveredEventArgs(PeerDescriptor peer, bool isNew)
    {
        Peer = peer;
        IsNew = isNew;
    }
}

public sealed class PeerConnectionEventArgs : EventArgs
{
    public PeerDescriptor Peer { get; }

    public PeerConnectionEventArgs(PeerDescriptor peer) => Peer = peer;
}

public sealed class PeerMessageEventArgs : EventArgs
{
    public string PeerId { get; }
    public long Sequence { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public DateTimeOffset Timestamp { get; }

    public PeerMessageEventArgs(string peerId, long sequence, ReadOnlyMemory<byte> payload, DateTimeOffset timestamp)
    {
        PeerId = peerId;
        Sequence = sequence;
        Payload = payload;
        Timestamp = timestamp;
    }
}

public sealed class PeerSecurityEventArgs : EventArgs
{
    public string PeerId { get; }
    public string Reason { get; }

    public PeerSecurityEventArgs(string peerId, string reason)
    {
        PeerId = peerId;
        Reason = reason;
    }
}
