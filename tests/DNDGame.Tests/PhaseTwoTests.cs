#nullable enable
using System.Net;
using System.Text;
using DNDGame.Services.Interfaces;
using DNDGame.Services.Llm;
using DNDGame.Services.Settings;
using DNDGame.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DNDGame.Tests;

public class PhaseTwoTests
{
    [Fact]
    public async Task SettingsService_SecureStorageRoundtrip()
    {
        ISecureStorageProvider store = new InMemorySecureStorageProvider();
        var svc = new SettingsService(store);

        await svc.SaveOpenAiApiKeyAsync("sk-test-123");
        (await svc.HasOpenAiApiKeyAsync()).Should().BeTrue();
        (await svc.GetOpenAiApiKeyAsync()).Should().Be("sk-test-123");

        await svc.DeleteOpenAiApiKeyAsync();
        (await svc.HasOpenAiApiKeyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiLlmService_SendsAuthorizationHeader()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var settings = new FakeSettingsService { Provider = "OpenAI", Model = "gpt-test" };
        await settings.SaveOpenAiApiKeyAsync("sk-secret");

        var service = new OpenAiLlmService(client, settings, new BasicLlmSafetyFilter(), NullLogger<OpenAiLlmService>.Instance);
        handler.ResponseContent = "{\"choices\":[{\"message\":{\"content\":\"pong\"}}]}";

        var response = await service.CompleteAsync("Say pong.");
        response.Should().Contain("pong");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("sk-secret");
        handler.LastRequest!.RequestUri!.ToString().Should().EndWith("chat/completions");
        handler.RecordedBody.Should().Contain("\"model\":\"gpt-test\"");
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private string? _apiKey;

        public IReadOnlyList<string> SupportedProviders { get; } = new[] { "OpenAI" };

        public string Provider { get; set; } = "OpenAI";
        public string Model { get; set; } = "gpt-4o-mini";

        public Task SaveOpenAiApiKeyAsync(string apiKey, CancellationToken ct = default)
        {
            _apiKey = apiKey;
            return Task.CompletedTask;
        }

        public Task<string?> GetOpenAiApiKeyAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(_apiKey);

        public Task<bool> HasOpenAiApiKeyAsync(CancellationToken ct = default)
            => Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey));

        public Task DeleteOpenAiApiKeyAsync(CancellationToken ct = default)
        {
            _apiKey = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string RecordedBody { get; private set; } = string.Empty;
        public string ResponseContent { get; set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                RecordedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.IsNullOrWhiteSpace(ResponseContent)
                    ? "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"
                    : ResponseContent, Encoding.UTF8, "application/json")
            };
            return message;
        }
    }
}
