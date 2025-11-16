#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ISecureStorageProvider
{
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    bool Remove(string key);
}
