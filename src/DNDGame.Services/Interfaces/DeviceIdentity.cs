#nullable enable
namespace DNDGame.Services.Interfaces;

public sealed record DeviceIdentity(
    string PeerId,
    string DeviceName,
    byte[] IdentityPublicKey,
    byte[] KeyExchangePublicKey)
{
    public override string ToString() => $"{DeviceName} ({PeerId})";
}
