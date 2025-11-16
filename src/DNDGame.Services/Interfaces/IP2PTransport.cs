#nullable enable
namespace DNDGame.Services.Interfaces;

public interface IP2PTransport
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
