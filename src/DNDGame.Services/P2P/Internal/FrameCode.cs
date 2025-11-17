#nullable enable
namespace DNDGame.Services.P2P.Internal;

internal enum FrameCode : byte
{
    HandshakeHello = 1,
    HandshakeAck = 2,
    Data = 3,
    Ack = 4,
    Close = 5,
    Heartbeat = 6
}
