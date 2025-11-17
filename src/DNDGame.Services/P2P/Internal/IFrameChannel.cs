#nullable enable
namespace DNDGame.Services.P2P.Internal;

internal interface IFrameChannel
{
    ValueTask WriteAsync(FrameCode code, ReadOnlyMemory<byte> payload, CancellationToken ct);
    ValueTask<(FrameCode Code, byte[] Payload)> ReadAsync(CancellationToken ct);
}
