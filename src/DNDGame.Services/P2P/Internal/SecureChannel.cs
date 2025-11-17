#nullable enable
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DNDGame.Services.P2P.Internal;

internal sealed record SecurePacket(long Sequence, byte[] Nonce, byte[] Ciphertext, byte[] Tag);

internal sealed class SecureChannel : IDisposable
{
    private readonly AesGcm _sendCipher;
    private readonly AesGcm _receiveCipher;
    private readonly byte[] _sessionAssociatedData;
    private readonly byte[] _nonceSalt = new byte[4];
    private long _sendSequence;
    private long _lastReceiveSequence = -1;
    private readonly Queue<long> _recentSequences = new();
    private readonly HashSet<long> _recentLookup = new();
    private readonly object _lock = new();

    public SecureChannel(ReadOnlySpan<byte> sendKey, ReadOnlySpan<byte> receiveKey, Guid sessionId)
    {
        _sendCipher = new AesGcm(sendKey.ToArray(), 16);
        _receiveCipher = new AesGcm(receiveKey.ToArray(), 16);
        _sessionAssociatedData = new byte[16];
        sessionId.TryWriteBytes(_sessionAssociatedData);
        RandomNumberGenerator.Fill(_nonceSalt);
    }

    public SecurePacket Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var sequence = Interlocked.Increment(ref _sendSequence);
        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(sequence, nonce);
        var ciphertext = new byte[plaintext.Length];
        Span<byte> tag = stackalloc byte[16];
        _sendCipher.Encrypt(nonce, plaintext, ciphertext, tag, _sessionAssociatedData);
        return new SecurePacket(sequence, nonce.ToArray(), ciphertext, tag.ToArray());
    }

    public ReadOnlyMemory<byte> Decrypt(long sequence, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        lock (_lock)
        {
            if (sequence <= _lastReceiveSequence || _recentLookup.Contains(sequence))
            {
                throw new CryptographicException("Replay detected");
            }

            _recentLookup.Add(sequence);
            _recentSequences.Enqueue(sequence);
            if (_recentSequences.Count > 64)
            {
                var removed = _recentSequences.Dequeue();
                _recentLookup.Remove(removed);
            }

            if (sequence > _lastReceiveSequence)
            {
                _lastReceiveSequence = sequence;
            }
        }

        var plaintext = new byte[ciphertext.Length];
        _receiveCipher.Decrypt(nonce, ciphertext, tag, plaintext, _sessionAssociatedData);
        return plaintext;
    }

    private void BuildNonce(long sequence, Span<byte> destination)
    {
        destination.Clear();
        _nonceSalt.CopyTo(destination);
        BinaryPrimitives.WriteInt64BigEndian(destination[^8..], sequence);
    }

    public void Dispose()
    {
        _sendCipher.Dispose();
        _receiveCipher.Dispose();
    }
}
