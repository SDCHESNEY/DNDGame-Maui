#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ISyncEngine
{
    Task InitializeAsync(CancellationToken ct = default);
}
