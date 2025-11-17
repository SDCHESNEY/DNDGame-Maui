#nullable enable
using System.Threading.Channels;

namespace DNDGame.Services.P2P.Internal;

internal sealed record LoopbackFrame(FrameCode Code, byte[] Payload);

internal sealed class LoopbackFrameChannel : IFrameChannel
{
    private readonly ChannelWriter<LoopbackFrame> _writer;
    private readonly ChannelReader<LoopbackFrame> _reader;

    private LoopbackFrameChannel(ChannelWriter<LoopbackFrame> writer, ChannelReader<LoopbackFrame> reader)
    {
        _writer = writer;
        _reader = reader;
    }

    public static (LoopbackFrameChannel A, LoopbackFrameChannel B) CreatePair()
    {
        var first = Channel.CreateUnbounded<LoopbackFrame>();
        var second = Channel.CreateUnbounded<LoopbackFrame>();
        return (new LoopbackFrameChannel(first.Writer, second.Reader),
                new LoopbackFrameChannel(second.Writer, first.Reader));
    }

    public async ValueTask WriteAsync(FrameCode code, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var buffer = payload.Length == 0 ? Array.Empty<byte>() : payload.ToArray();
        await _writer.WriteAsync(new LoopbackFrame(code, buffer), ct).ConfigureAwait(false);
    }

    public async ValueTask<(FrameCode Code, byte[] Payload)> ReadAsync(CancellationToken ct)
    {
        var frame = await _reader.ReadAsync(ct).ConfigureAwait(false);
        return (frame.Code, frame.Payload);
    }
}
