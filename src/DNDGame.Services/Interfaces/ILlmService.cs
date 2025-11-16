#nullable enable
namespace DNDGame.Services.Interfaces;

public interface ILlmService
{
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamCompletionAsync(string prompt, CancellationToken ct = default);
}
