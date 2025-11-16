#nullable enable
using DNDGame.Services.Interfaces;

namespace DNDGame.Tests.Fakes;

public sealed class InMemorySecureStorageProvider : ISecureStorageProvider
{
    private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public bool Remove(string key) => _store.Remove(key);
}
