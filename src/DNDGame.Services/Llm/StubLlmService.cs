#nullable enable
using DNDGame.Services.Interfaces;

namespace DNDGame.Services.Llm;

public class StubLlmService : ILlmService
{
    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult($"[LLM STUB RESPONSE] {prompt}");
}
