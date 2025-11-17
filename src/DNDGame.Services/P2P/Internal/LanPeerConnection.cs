#nullable enable
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DNDGame.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DNDGame.Services.P2P.Internal;

internal sealed class LanPeerConnection : IAsyncDisposable
{
    private const int Curve25519KeySize = 32;
    private const int SharedSecretSize = 32;
    private const int SignatureSize = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IFrameChannel _channel;
    private readonly SecureChannel _secureChannel;
    private readonly ICryptoService _crypto;
    private readonly LanP2PTransportOptions _options;
    private readonly ILogger _logger;
    private readonly Func<PeerMessageEventArgs, Task> _messageCallback;
    private readonly Action<PeerConnectionEventArgs> _disconnected;
    private readonly Action<PeerSecurityEventArgs> _securityCallback;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _pendingAcks = new();
    private readonly Task _readLoop;

    private LanPeerConnection(
        IFrameChannel channel,
        SecureChannel secureChannel,
        ICryptoService crypto,
        LanP2PTransportOptions options,
        ILogger logger,
        Func<PeerMessageEventArgs, Task> messageCallback,
        Action<PeerConnectionEventArgs> disconnected,
        Action<PeerSecurityEventArgs> securityCallback,
        PeerDescriptor descriptor,
        Guid sessionId)
    {
        _channel = channel;
        _secureChannel = secureChannel;
        _crypto = crypto;
        _options = options;
        _logger = logger;
        _messageCallback = messageCallback;
        _disconnected = disconnected;
        _securityCallback = securityCallback;
        Descriptor = descriptor;
        SessionId = sessionId;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public PeerDescriptor Descriptor { get; }
    public Guid SessionId { get; }

    public static async Task<LanPeerConnection> CreateInitiatorAsync(
        IFrameChannel channel,
        ICryptoService crypto,
        LanP2PTransportOptions options,
        ILogger logger,
        Func<PeerMessageEventArgs, Task> messageCallback,
        Action<PeerConnectionEventArgs> disconnected,
        Action<PeerSecurityEventArgs> securityCallback,
        string remoteHost,
        PeerDescriptor? cachedDescriptor,
        CancellationToken ct)
    {
        var sessionId = Guid.NewGuid();
        var ephPrivate = new byte[Curve25519KeySize];
        var ephPublic = new byte[Curve25519KeySize];
        crypto.GenerateEphemeralKeyPair(ephPrivate, ephPublic);

        var identityKey = Convert.ToBase64String(crypto.IdentityPublicKey.Span);
        var keyExchangeKey = Convert.ToBase64String(crypto.KeyExchangePublicKey.Span);
        var hello = new HandshakePayload
        {
            SessionId = sessionId,
            PeerId = crypto.Identity.PeerId,
            DeviceName = crypto.Identity.DeviceName,
            IdentityKey = identityKey,
            KeyExchangeKey = keyExchangeKey,
            EphemeralKey = Convert.ToBase64String(ephPublic),
            Signature = Convert.ToBase64String(Sign(crypto, sessionId, ephPublic, crypto.KeyExchangePublicKey.Span))
        };

        var helloBytes = JsonSerializer.SerializeToUtf8Bytes(hello, SerializerOptions);
        await channel.WriteAsync(FrameCode.HandshakeHello, helloBytes, ct).ConfigureAwait(false);

        var (code, payload) = await channel.ReadAsync(ct).ConfigureAwait(false);
        if (code != FrameCode.HandshakeAck)
        {
            throw new InvalidOperationException("Peer did not acknowledge handshake");
        }

        var ack = JsonSerializer.Deserialize<HandshakePayload>(payload, SerializerOptions)
                  ?? throw new InvalidOperationException("Invalid handshake ack");
        if (ack.SessionId != sessionId)
        {
            throw new CryptographicException("Session identifier mismatch");
        }
        ValidatePeerIdentity(crypto, ack);
        var remoteIdentityKey = Convert.FromBase64String(ack.IdentityKey);
        var remoteKeyExchange = Convert.FromBase64String(ack.KeyExchangeKey);
        var remoteEphemeral = Convert.FromBase64String(ack.EphemeralKey);
        var signature = Convert.FromBase64String(ack.Signature);
        var signedData = BuildSignedPayload(sessionId, remoteEphemeral, remoteKeyExchange);
        if (!crypto.Verify(signedData, signature, remoteIdentityKey))
        {
            throw new CryptographicException("Handshake signature invalid");
        }

        var transcript = SHA256.HashData(Concat(helloBytes, payload));
        var (sendKey, receiveKey) = DeriveKeys(crypto, ephPrivate, remoteEphemeral, remoteKeyExchange, transcript, true);
        var secureChannel = new SecureChannel(sendKey, receiveKey, sessionId);

        var descriptor = cachedDescriptor ?? new PeerDescriptor(
            ack.PeerId,
            ack.DeviceName,
            ack.IdentityKey,
            ack.KeyExchangeKey,
            remoteHost,
            0,
            DateTimeOffset.UtcNow);

        return new LanPeerConnection(channel, secureChannel, crypto, options, logger, messageCallback, disconnected, securityCallback, descriptor, sessionId);
    }

    public static async Task<LanPeerConnection> CreateResponderAsync(
        IFrameChannel channel,
        ICryptoService crypto,
        LanP2PTransportOptions options,
        ILogger logger,
        Func<PeerMessageEventArgs, Task> messageCallback,
        Action<PeerConnectionEventArgs> disconnected,
        Action<PeerSecurityEventArgs> securityCallback,
        string remoteHost,
        CancellationToken ct)
    {
        var (code, payload) = await channel.ReadAsync(ct).ConfigureAwait(false);
        if (code != FrameCode.HandshakeHello)
        {
            throw new InvalidOperationException("Handshake hello expected");
        }

        var hello = JsonSerializer.Deserialize<HandshakePayload>(payload, SerializerOptions)
                ?? throw new InvalidOperationException("Invalid handshake hello");
        ValidatePeerIdentity(crypto, hello);
        var sessionId = hello.SessionId;
        var remoteIdentityKey = Convert.FromBase64String(hello.IdentityKey);
        var remoteKeyExchange = Convert.FromBase64String(hello.KeyExchangeKey);
        var remoteEphemeral = Convert.FromBase64String(hello.EphemeralKey);
        var signature = Convert.FromBase64String(hello.Signature);
        var signedData = BuildSignedPayload(sessionId, remoteEphemeral, remoteKeyExchange);
        if (!crypto.Verify(signedData, signature, remoteIdentityKey))
        {
            throw new CryptographicException("Handshake signature invalid");
        }

        var ephPrivate = new byte[Curve25519KeySize];
        var ephPublic = new byte[Curve25519KeySize];
        crypto.GenerateEphemeralKeyPair(ephPrivate, ephPublic);

        var ack = new HandshakePayload
        {
            SessionId = sessionId,
            PeerId = crypto.Identity.PeerId,
            DeviceName = crypto.Identity.DeviceName,
            IdentityKey = Convert.ToBase64String(crypto.IdentityPublicKey.Span),
            KeyExchangeKey = Convert.ToBase64String(crypto.KeyExchangePublicKey.Span),
            EphemeralKey = Convert.ToBase64String(ephPublic),
            Signature = Convert.ToBase64String(Sign(crypto, sessionId, ephPublic, crypto.KeyExchangePublicKey.Span))
        };

        var ackBytes = JsonSerializer.SerializeToUtf8Bytes(ack, SerializerOptions);
        await channel.WriteAsync(FrameCode.HandshakeAck, ackBytes, ct).ConfigureAwait(false);

        var transcript = SHA256.HashData(Concat(payload, ackBytes));
        var (sendKey, receiveKey) = DeriveKeys(crypto, ephPrivate, remoteEphemeral, remoteKeyExchange, transcript, false);
        var secureChannel = new SecureChannel(sendKey, receiveKey, sessionId);

        var descriptor = new PeerDescriptor(
            hello.PeerId,
            hello.DeviceName,
            hello.IdentityKey,
            hello.KeyExchangeKey,
            remoteHost,
            0,
            DateTimeOffset.UtcNow);

        return new LanPeerConnection(channel, secureChannel, crypto, options, logger, messageCallback, disconnected, securityCallback, descriptor, sessionId);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var packet = _secureChannel.Encrypt(payload.Span);
        var buffer = SerializePacket(packet);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks[packet.Sequence] = tcs;
        await _channel.WriteAsync(FrameCode.Data, buffer, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.AckTimeout);
        using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false))
        {
            await tcs.Task.ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var (code, payload) = await _channel.ReadAsync(_cts.Token).ConfigureAwait(false);
                switch (code)
                {
                    case FrameCode.Data:
                        await HandleDataAsync(payload).ConfigureAwait(false);
                        break;
                    case FrameCode.Ack:
                        HandleAck(payload);
                        break;
                    case FrameCode.Close:
                        _cts.Cancel();
                        return;
                    case FrameCode.Heartbeat:
                        break;
                    default:
                        _logger.LogWarning("Unknown frame {Code} from {Peer}", code, Descriptor.PeerId);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "P2P connection to {Peer} ended unexpectedly", Descriptor.PeerId);
        }
        finally
        {
            _disconnected(new PeerConnectionEventArgs(Descriptor));
        }
    }

    private async Task HandleDataAsync(byte[] payload)
    {
        try
        {
            var packet = DeserializePacket(payload);
            var plaintext = _secureChannel.Decrypt(packet.Sequence, packet.Nonce, packet.Ciphertext, packet.Tag);
            await _channel.WriteAsync(FrameCode.Ack, SerializeAck(packet.Sequence), _cts.Token).ConfigureAwait(false);
            await _messageCallback(new PeerMessageEventArgs(Descriptor.PeerId, packet.Sequence, plaintext, DateTimeOffset.UtcNow)).ConfigureAwait(false);
        }
        catch (CryptographicException ex)
        {
            _securityCallback(new PeerSecurityEventArgs(Descriptor.PeerId, ex.Message));
        }
    }

    private void HandleAck(byte[] payload)
    {
        if (payload.Length < sizeof(long))
        {
            return;
        }

        var sequence = BinaryPrimitives.ReadInt64BigEndian(payload);
        if (_pendingAcks.TryRemove(sequence, out var tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    private static byte[] SerializePacket(SecurePacket packet)
    {
        var buffer = new byte[sizeof(long) + 12 + sizeof(int) + packet.Ciphertext.Length + packet.Tag.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt64BigEndian(span[..sizeof(long)], packet.Sequence);
        packet.Nonce.CopyTo(span.Slice(sizeof(long), 12));
        var offset = sizeof(long) + 12;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, sizeof(int)), packet.Ciphertext.Length);
        offset += sizeof(int);
        packet.Ciphertext.CopyTo(span[offset..]);
        offset += packet.Ciphertext.Length;
        packet.Tag.CopyTo(span[offset..]);
        return buffer;
    }

    private static SecurePacket DeserializePacket(byte[] buffer)
    {
        var span = buffer.AsSpan();
        var sequence = BinaryPrimitives.ReadInt64BigEndian(span[..sizeof(long)]);
        var nonce = span.Slice(sizeof(long), 12).ToArray();
        var offset = sizeof(long) + 12;
        var cipherLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        var ciphertext = span.Slice(offset, cipherLength).ToArray();
        offset += cipherLength;
        var tag = span[offset..].ToArray();
        return new SecurePacket(sequence, nonce, ciphertext, tag);
    }

    private static byte[] SerializeAck(long sequence)
    {
        var buffer = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(buffer, sequence);
        return buffer;
    }

    private static void ValidatePeerIdentity(ICryptoService crypto, HandshakePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.PeerId))
        {
            throw new InvalidOperationException("PeerId missing");
        }

        var computed = crypto.GetPeerId(Convert.FromBase64String(payload.IdentityKey));
        if (!string.Equals(payload.PeerId, computed, StringComparison.Ordinal))
        {
            throw new CryptographicException("Peer identity mismatch");
        }
    }

    private static byte[] BuildSignedPayload(Guid sessionId, ReadOnlySpan<byte> ephemeral, ReadOnlySpan<byte> keyExchange)
    {
        Span<byte> sessionBytes = stackalloc byte[16];
        sessionId.TryWriteBytes(sessionBytes);
        var buffer = new byte[sessionBytes.Length + ephemeral.Length + keyExchange.Length];
        sessionBytes.CopyTo(buffer);
        ephemeral.CopyTo(buffer.AsSpan(sessionBytes.Length));
        keyExchange.CopyTo(buffer.AsSpan(sessionBytes.Length + ephemeral.Length));
        return buffer;
    }

    private static byte[] Sign(ICryptoService crypto, Guid sessionId, ReadOnlySpan<byte> ephemeral, ReadOnlySpan<byte> keyExchange)
    {
        Span<byte> signature = stackalloc byte[SignatureSize];
        crypto.Sign(BuildSignedPayload(sessionId, ephemeral, keyExchange), signature);
        return signature.ToArray();
    }

    private static (byte[] SendKey, byte[] ReceiveKey) DeriveKeys(
        ICryptoService crypto,
        ReadOnlySpan<byte> localEphemeralPrivate,
        ReadOnlySpan<byte> remoteEphemeralPublic,
        ReadOnlySpan<byte> remoteKeyExchangePublic,
        ReadOnlySpan<byte> transcript,
        bool initiator)
    {
        Span<byte> material = stackalloc byte[SharedSecretSize * 4];
        var offset = 0;
        crypto.ComputeSharedSecret(localEphemeralPrivate, remoteEphemeralPublic, material.Slice(offset, SharedSecretSize));
        offset += SharedSecretSize;
        crypto.ComputeSharedSecret(localEphemeralPrivate, remoteKeyExchangePublic, material.Slice(offset, SharedSecretSize));
        offset += SharedSecretSize;
        crypto.ComputeStaticSharedSecret(remoteEphemeralPublic, material.Slice(offset, SharedSecretSize));
        offset += SharedSecretSize;
        crypto.ComputeStaticSharedSecret(remoteKeyExchangePublic, material.Slice(offset, SharedSecretSize));

        if (!initiator)
        {
            Span<byte> first = material.Slice(SharedSecretSize, SharedSecretSize);
            Span<byte> second = material.Slice(SharedSecretSize * 2, SharedSecretSize);
            Span<byte> temp = stackalloc byte[SharedSecretSize];
            first.CopyTo(temp);
            second.CopyTo(first);
            temp.CopyTo(second);
        }

        Span<byte> keyMaterial = stackalloc byte[64];
        DeriveKey(material, transcript, "dndgame:p2p"u8, keyMaterial);

        if (initiator)
        {
            return (keyMaterial[..32].ToArray(), keyMaterial[32..64].ToArray());
        }

        return (keyMaterial[32..64].ToArray(), keyMaterial[..32].ToArray());
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var buffer = new byte[first.Length + second.Length];
        first.CopyTo(buffer, 0);
        second.CopyTo(buffer, first.Length);
        return buffer;
    }

    private static void DeriveKey(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, Span<byte> destination)
    {
        var saltKey = salt.IsEmpty ? Array.Empty<byte>() : salt.ToArray();
        using var hmac = new HMACSHA256(saltKey);
        var prk = hmac.ComputeHash(ikm.ToArray());
        var infoBytes = info.ToArray();
        var t = Array.Empty<byte>();
        var generated = 0;
        byte counter = 1;

        while (generated < destination.Length)
        {
            var input = new byte[t.Length + infoBytes.Length + 1];
            Buffer.BlockCopy(t, 0, input, 0, t.Length);
            Buffer.BlockCopy(infoBytes, 0, input, t.Length, infoBytes.Length);
            input[^1] = counter++;

            hmac.Key = prk;
            t = hmac.ComputeHash(input);
            var toCopy = Math.Min(t.Length, destination.Length - generated);
            t.AsSpan(0, toCopy).CopyTo(destination.Slice(generated));
            generated += toCopy;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _secureChannel.Dispose();
    }

    private sealed record HandshakePayload
    {
        public Guid SessionId { get; init; }
        public string PeerId { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string IdentityKey { get; init; } = string.Empty;
        public string KeyExchangeKey { get; init; } = string.Empty;
        public string EphemeralKey { get; init; } = string.Empty;
        public string Signature { get; init; } = string.Empty;
    }
}
