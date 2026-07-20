using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Providers;

/// <summary>Talks to Groq's OpenAI-compatible Chat Completions API (https://api.groq.com/openai/v1/chat/completions) — cheap/fast Llama inference.</summary>
public class GroqProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqProvider> _logger;

    public LlmProviderType ProviderType => LlmProviderType.Groq;

    public GroqProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<GroqProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Groq;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("CONTENTWRITER__GROG__KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentGenerationException(
                "Groq API key is not configured. Set GROQ_API_KEY (or LlmProviders__Groq__ApiKey).");
        }

        var model = request.Model
            ?? Environment.GetEnvironmentVariable("CONTENTWRITER__GROG__MODEL")
            ?? _options.Model;

        var payload = new OpenAiCompatibleRequest
        {
            Model = model,
            Messages = request.Messages.Select(m => new OpenAiCompatibleMessage(m.RoleString, m.Content)).ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxOutputTokens
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException("Could not reach the Groq API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Groq returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"Groq request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiCompatibleResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("Groq returned an empty/unparseable response.");

        var choice = parsed.Choices.FirstOrDefault()
            ?? throw new ContentGenerationException("Groq response contained no choices.");

        return new ChatCompletionResult(
            Content: choice.Message.Content,
            ModelUsed: parsed.Model ?? model,
            PromptTokens: parsed.Usage?.PromptTokens,
            CompletionTokens: parsed.Usage?.CompletionTokens);
    }
}
