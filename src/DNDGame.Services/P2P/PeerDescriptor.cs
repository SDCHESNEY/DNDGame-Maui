#nullable enable
using System.Net;

namespace DNDGame.Services.P2P;

public sealed record PeerDescriptor(
    string PeerId,
    string DeviceName,
    string IdentityPublicKey,
    string KeyExchangePublicKey,
    string Host,
    int Port,
    DateTimeOffset LastSeen)
{
    public bool TryGetEndpoint(out IPEndPoint endpoint)
    {
        if (IPAddress.TryParse(Host, out var ip))
        {
            endpoint = new IPEndPoint(ip, Port);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.Loopback, Port);
        return false;
    }
}
