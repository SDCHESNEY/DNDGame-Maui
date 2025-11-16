#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ISettingsService
{
    IReadOnlyList<string> SupportedProviders { get; }

    string Provider { get; set; }
    string Model { get; set; }

    Task SaveOpenAiApiKeyAsync(string apiKey, CancellationToken ct = default);
    Task<string?> GetOpenAiApiKeyAsync(CancellationToken ct = default);
    Task<bool> HasOpenAiApiKeyAsync(CancellationToken ct = default);
    Task DeleteOpenAiApiKeyAsync(CancellationToken ct = default);
}
