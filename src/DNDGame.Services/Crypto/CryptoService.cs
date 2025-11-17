#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using DNDGame.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using NSec.Cryptography;

namespace DNDGame.Services.Crypto;

public sealed class CryptoService : ICryptoService
{
    private const string IdentityKeyStorage = "crypto:ed25519";
    private const string KeyExchangeStorage = "crypto:x25519";
    private const string DeviceNamePreferenceKey = "crypto:deviceName";

    private static readonly SignatureAlgorithm SigningAlgorithm = SignatureAlgorithm.Ed25519;
    private static readonly KeyAgreementAlgorithm AgreementAlgorithm = KeyAgreementAlgorithm.X25519;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ISecureStorageProvider _secureStorage;
    private readonly ILogger<CryptoService> _logger;

    private DeviceIdentity? _identity;
    private Key? _signingKey;
    private Key? _agreementKey;
    private byte[]? _identityPublicKey;
    private byte[]? _agreementPublicKey;

    public CryptoService(ISecureStorageProvider secureStorage, ILogger<CryptoService> logger)
    {
        _secureStorage = secureStorage;
        _logger = logger;
    }

    public DeviceIdentity Identity => _identity ?? throw new InvalidOperationException("CryptoService not initialized");

    public ReadOnlyMemory<byte> IdentityPublicKey => _identityPublicKey ?? throw new InvalidOperationException("CryptoService not initialized");
    public ReadOnlyMemory<byte> KeyExchangePublicKey => _agreementPublicKey ?? throw new InvalidOperationException("CryptoService not initialized");

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_identity is not null)
        {
            return;
        }

        var signingPair = await LoadOrCreateKeyPairAsync(IdentityKeyStorage, () => GenerateKeyPair(SigningAlgorithm), ct).ConfigureAwait(false);
        var agreementPair = await LoadOrCreateKeyPairAsync(KeyExchangeStorage, () => GenerateKeyPair(AgreementAlgorithm), ct).ConfigureAwait(false);

        _signingKey = Key.Import(SigningAlgorithm, signingPair.PrivateKey, KeyBlobFormat.RawPrivateKey, CreateKeyParameters());
        _identityPublicKey = signingPair.PublicKey;

        _agreementKey = Key.Import(AgreementAlgorithm, agreementPair.PrivateKey, KeyBlobFormat.RawPrivateKey, CreateKeyParameters());
        _agreementPublicKey = agreementPair.PublicKey;

        var peerId = GetPeerId(_identityPublicKey);
        var deviceName = GetDeviceNamePreference(ResolveDeviceName());
        SetDeviceNamePreference(deviceName);
        _identity = new DeviceIdentity(peerId, deviceName, _identityPublicKey, _agreementPublicKey);
    }

    public void Sign(ReadOnlySpan<byte> data, Span<byte> signature)
    {
        if (_signingKey is null)
        {
            throw new InvalidOperationException("CryptoService not initialized");
        }

        var signed = SigningAlgorithm.Sign(_signingKey, data);
        signed.CopyTo(signature);
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> identityPublicKey)
    {
        var publicKey = PublicKey.Import(SigningAlgorithm, identityPublicKey, KeyBlobFormat.RawPublicKey);
        return SigningAlgorithm.Verify(publicKey, data, signature);
    }

    public void GenerateEphemeralKeyPair(Span<byte> privateKey, Span<byte> publicKey)
    {
        using var key = Key.Create(AgreementAlgorithm, CreateKeyParameters());
        var priv = key.Export(KeyBlobFormat.RawPrivateKey);
        var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        priv.CopyTo(privateKey);
        pub.CopyTo(publicKey);
    }

    public void ComputeSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> remotePublicKey, Span<byte> destination)
    {
        using var key = Key.Import(AgreementAlgorithm, privateKey, KeyBlobFormat.RawPrivateKey, CreateKeyParameters());
        var publicKey = PublicKey.Import(AgreementAlgorithm, remotePublicKey, KeyBlobFormat.RawPublicKey);
        using var shared = AgreementAlgorithm.Agree(key, publicKey) ?? throw new CryptographicException("Unable to derive shared secret");
        KeyDerivationAlgorithm.HkdfSha256.DeriveBytes(shared, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, destination);
    }

    public void ComputeStaticSharedSecret(ReadOnlySpan<byte> remoteKeyExchangePublicKey, Span<byte> destination)
    {
        if (_agreementKey is null)
        {
            throw new InvalidOperationException("CryptoService not initialized");
        }

        var publicKey = PublicKey.Import(AgreementAlgorithm, remoteKeyExchangePublicKey, KeyBlobFormat.RawPublicKey);
        using var shared = AgreementAlgorithm.Agree(_agreementKey, publicKey) ?? throw new CryptographicException("Unable to derive shared secret");
        KeyDerivationAlgorithm.HkdfSha256.DeriveBytes(shared, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, destination);
    }

    public string GetPeerId(ReadOnlySpan<byte> identityPublicKey)
    {
        Span<byte> hash = stackalloc byte[32];
        if (!SHA256.TryHashData(identityPublicKey, hash, out _))
        {
            throw new CryptographicException("Unable to hash identity key");
        }

        Span<char> buffer = stackalloc char[10];
        EncodeBase32(hash[..6], buffer);
        return new string(buffer);
    }

    private async Task<KeyPair> LoadOrCreateKeyPairAsync(string storageKey, Func<KeyPair> factory, CancellationToken ct)
    {
        var existing = await _secureStorage.GetAsync(storageKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<KeyPairPayload>(existing, SerializerOptions);
                if (payload is not null)
                {
                    return new KeyPair(Convert.FromBase64String(payload.PrivateKey), Convert.FromBase64String(payload.PublicKey));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stored key {Key} invalid, regenerating", storageKey);
            }
        }

        var generated = factory();
        var serialized = JsonSerializer.Serialize(new KeyPairPayload(Convert.ToBase64String(generated.PublicKey), Convert.ToBase64String(generated.PrivateKey)), SerializerOptions);
        await _secureStorage.SetAsync(storageKey, serialized, ct).ConfigureAwait(false);
        return generated;
    }

    private static KeyPair GenerateKeyPair(SignatureAlgorithm algorithm)
    {
        using var key = Key.Create(algorithm, CreateKeyParameters());
        return new KeyPair(
            key.Export(KeyBlobFormat.RawPrivateKey),
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    private static KeyPair GenerateKeyPair(KeyAgreementAlgorithm algorithm)
    {
        using var key = Key.Create(algorithm, CreateKeyParameters());
        return new KeyPair(
            key.Export(KeyBlobFormat.RawPrivateKey),
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    private static KeyCreationParameters CreateKeyParameters()
        => new()
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        };

    private static string ResolveDeviceName()
    {
        try
        {
            var name = DeviceInfo.Current.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            // ignored (not running within MAUI context)
        }

        var machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine))
        {
            return machine;
        }

        return $"Peer-{RandomNumberGenerator.GetInt32(1000, 9999)}";
    }

    private static void EncodeBase32(ReadOnlySpan<byte> data, Span<char> destination)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        int bitBuffer = 0;
        int bitCount = 0;
        int destIndex = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5 && destIndex < destination.Length)
            {
                var value = (bitBuffer >> (bitCount - 5)) & 0x1F;
                destination[destIndex++] = alphabet[value];
                bitCount -= 5;
            }
        }

        if (bitCount > 0 && destIndex < destination.Length)
        {
            var value = (bitBuffer << (5 - bitCount)) & 0x1F;
            destination[destIndex++] = alphabet[value];
        }

        while (destIndex < destination.Length)
        {
            destination[destIndex++] = '0';
        }
    }

    private static string GetDeviceNamePreference(string fallback)
    {
        try
        {
            return Preferences.Get(DeviceNamePreferenceKey, fallback);
        }
        catch (Exception ex) when (IsPreferencesUnavailable(ex))
        {
            return fallback;
        }
    }

    private static void SetDeviceNamePreference(string value)
    {
        try
        {
            Preferences.Set(DeviceNamePreferenceKey, value);
        }
        catch (Exception ex) when (IsPreferencesUnavailable(ex))
        {
            // ignored in test contexts without Preferences implementation
        }
    }

    private static bool IsPreferencesUnavailable(Exception ex)
        => ex.GetType().Name is "NotImplementedInReferenceAssemblyException" or "FeatureNotSupportedException"
           || ex is PlatformNotSupportedException;

    private sealed record KeyPairPayload(string PublicKey, string PrivateKey);

    private readonly record struct KeyPair(byte[] PrivateKey, byte[] PublicKey);
}
