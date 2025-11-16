#nullable enable
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DNDGame.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace DNDGame.Services.Llm;

public class OpenAiLlmService : ILlmService
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1/";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILlmSafetyFilter _safetyFilter;
    private readonly ILogger<OpenAiLlmService> _logger;

    public OpenAiLlmService(HttpClient httpClient,
                            ISettingsService settingsService,
                            ILlmSafetyFilter safetyFilter,
                            ILogger<OpenAiLlmService> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(DefaultBaseUrl);
        _settingsService = settingsService;
        _safetyFilter = safetyFilter;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        _safetyFilter.EnsureAllowed(prompt);
        EnsureProviderIsOpenAi();
        var apiKey = await RequireApiKeyAsync(ct).ConfigureAwait(false);

        var body = CreateRequestBody(prompt, stream: false);
        using var request = BuildHttpRequest(apiKey, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowForHttpFailure(response.StatusCode, payload);
        }

        var completion = JsonSerializer.Deserialize<OpenAiChatResponse>(payload, SerializerOptions);
        return completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _safetyFilter.EnsureAllowed(prompt);
        EnsureProviderIsOpenAi();
        var apiKey = await RequireApiKeyAsync(ct).ConfigureAwait(false);

        var body = CreateRequestBody(prompt, stream: true);
        using var request = BuildHttpRequest(apiKey, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var failurePayload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ThrowForHttpFailure(response.StatusCode, failurePayload);
        }

        await foreach (var chunk in ReadStreamAsync(response, ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private OpenAiChatRequest CreateRequestBody(string prompt, bool stream) => new()
    {
        Model = _settingsService.Model,
        Stream = stream,
        Temperature = 0.7f,
        Messages =
        [
            new OpenAiChatMessage("system", "You are a helpful dungeon master assistant."),
            new OpenAiChatMessage("user", prompt)
        ]
    };

    private static HttpRequestMessage BuildHttpRequest(string apiKey, OpenAiChatRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Equals("data: [DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = line.Substring("data:".Length).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            OpenAiChatStreamResponse? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAiChatStreamResponse>(json, SerializerOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse OpenAI stream chunk");
            }

            if (chunk?.Choices is null)
            {
                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                var text = choice.Delta?.Content?.FirstOrDefault()?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    private void EnsureProviderIsOpenAi()
    {
        if (!string.Equals(_settingsService.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Active provider '{_settingsService.Provider}' is not supported yet.");
        }
    }

    private async Task<string> RequireApiKeyAsync(CancellationToken ct)
    {
        var apiKey = await _settingsService.GetOpenAiApiKeyAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured. Add one in Settings.");
        }
        return apiKey;
    }

    private void ThrowForHttpFailure(HttpStatusCode statusCode, string payload)
    {
        var sanitized = TryExtractErrorMessage(payload);
        _logger.LogWarning("OpenAI request failed with {StatusCode}: {Message}", statusCode, sanitized);
        throw new HttpRequestException($"OpenAI request failed ({(int)statusCode}): {sanitized}");
    }

    private static string TryExtractErrorMessage(string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<OpenAiErrorResponse>(payload, SerializerOptions);
            return error?.Error?.Message ?? "Unknown error";
        }
        catch
        {
            return "Unknown error";
        }
    }

    private sealed record OpenAiChatRequest
    {
        public string Model { get; init; } = string.Empty;
        public IReadOnlyList<OpenAiChatMessage> Messages { get; init; } = Array.Empty<OpenAiChatMessage>();
        public bool Stream { get; init; }
        public float Temperature { get; init; }
    }

    private sealed record OpenAiChatMessage(string Role, string Content);

    private sealed record OpenAiChatResponse
    {
        public IReadOnlyList<OpenAiChatChoice>? Choices { get; init; }
    }

    private sealed record OpenAiChatChoice
    {
        public OpenAiChatMessage Message { get; init; } = new("assistant", string.Empty);
    }

    private sealed record OpenAiChatStreamResponse
    {
        public IReadOnlyList<OpenAiChatStreamChoice>? Choices { get; init; }
    }

    private sealed record OpenAiChatStreamChoice
    {
        public OpenAiDelta? Delta { get; init; }
    }

    private sealed record OpenAiDelta
    {
        public IReadOnlyList<OpenAiDeltaContent>? Content { get; init; }
    }

    private sealed record OpenAiDeltaContent
    {
        public string? Text { get; init; }
    }

    private sealed record OpenAiErrorResponse
    {
        public OpenAiError? Error { get; init; }
    }

    private sealed record OpenAiError
    {
        public string? Message { get; init; }
    }
}
