#nullable enable
using System.Runtime.CompilerServices;
using DNDGame.Services.Interfaces;

namespace DNDGame.Services.Llm;

public class StubLlmService : ILlmService
{
    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult($"[LLM STUB RESPONSE] {prompt}");

    public async IAsyncEnumerable<string> StreamCompletionAsync(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await CompleteAsync(prompt, ct).ConfigureAwait(false);
        yield return response;
    }
}
