#nullable enable
using DNDGame.Services.Interfaces;
using Microsoft.Maui.Storage;

namespace DNDGame.Services.Settings;

public class SettingsService : ISettingsService
{
    private const string ProviderKey = "llm:provider";
    private const string ModelKey = "llm:openai:model";
    private const string ApiKeyKey = "llm:openai:apiKey";
    private static readonly string[] DefaultProviders = ["OpenAI", "Localhost", "OnDevice"];

    private readonly ISecureStorageProvider _secureStorage;

    public SettingsService(ISecureStorageProvider secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public IReadOnlyList<string> SupportedProviders => DefaultProviders;

    public string Provider
    {
        get => Preferences.Get(ProviderKey, "OpenAI");
        set => Preferences.Set(ProviderKey, value);
    }

    public string Model
    {
        get => Preferences.Get(ModelKey, "gpt-4o-mini");
        set => Preferences.Set(ModelKey, value);
    }

    public async Task SaveOpenAiApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));
        }

        await _secureStorage.SetAsync(ApiKeyKey, apiKey.Trim(), ct).ConfigureAwait(false);
    }

    public Task<string?> GetOpenAiApiKeyAsync(CancellationToken ct = default)
        => _secureStorage.GetAsync(ApiKeyKey, ct);

    public async Task<bool> HasOpenAiApiKeyAsync(CancellationToken ct = default)
    {
        var existing = await _secureStorage.GetAsync(ApiKeyKey, ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(existing);
    }

    public Task DeleteOpenAiApiKeyAsync(CancellationToken ct = default)
    {
        _secureStorage.Remove(ApiKeyKey);
        return Task.CompletedTask;
    }
}
