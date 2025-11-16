#nullable enable
using DNDGame.Services.Interfaces;

namespace DNDGame.Services.P2P;

public class DummyP2PTransport : IP2PTransport
{
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
