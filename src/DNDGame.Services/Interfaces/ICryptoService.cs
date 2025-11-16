#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ICryptoService
{
    string DevicePublicKey { get; }
    Task InitializeAsync(CancellationToken ct = default);
}
