#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ICryptoService
{
    DeviceIdentity Identity { get; }

    Task InitializeAsync(CancellationToken ct = default);

    ReadOnlyMemory<byte> IdentityPublicKey { get; }
    ReadOnlyMemory<byte> KeyExchangePublicKey { get; }

    void Sign(ReadOnlySpan<byte> data, Span<byte> signature);
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> identityPublicKey);

    void GenerateEphemeralKeyPair(Span<byte> privateKey, Span<byte> publicKey);
    void ComputeSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> remotePublicKey, Span<byte> destination);
    void ComputeStaticSharedSecret(ReadOnlySpan<byte> remoteKeyExchangePublicKey, Span<byte> destination);

    string GetPeerId(ReadOnlySpan<byte> identityPublicKey);
}
