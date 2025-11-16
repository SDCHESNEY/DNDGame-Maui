#nullable enable
using DNDGame.Services.Interfaces;
using Microsoft.Maui.Storage;

namespace DNDGame.Services.Settings;

public class SecureStorageProvider : ISecureStorageProvider
{
    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(key, value);
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync(key);
    }

    public bool Remove(string key) => SecureStorage.Default.Remove(key);
}
