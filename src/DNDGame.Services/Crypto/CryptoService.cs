#nullable enable
using DNDGame.Services.Interfaces;
using System.Security.Cryptography;

namespace DNDGame.Services.Crypto;

public class CryptoService : ICryptoService
{
    private string? _publicKey;

    public string DevicePublicKey => _publicKey ?? string.Empty;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        // Placeholder: generate random 32-byte key and Base64 encode.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        _publicKey = Convert.ToBase64String(bytes);
        return Task.CompletedTask;
    }
}
