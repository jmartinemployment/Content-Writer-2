using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Providers;

/// <summary>Talks to the OpenAI Chat Completions API (https://api.openai.com/v1/chat/completions).</summary>
public class OpenAiProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiProvider> _logger;

    public LlmProviderType ProviderType => LlmProviderType.OpenAi;

    public OpenAiProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.OpenAi;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ContentGenerationException("OpenAI API key is not configured (LlmProviders:OpenAi:ApiKey).");
        }

        var payload = new OpenAiCompatibleRequest
        {
            Model = request.Model ?? _options.Model,
            Messages = request.Messages.Select(m => new OpenAiCompatibleMessage(m.RoleString, m.Content)).ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxOutputTokens
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException("Could not reach the OpenAI API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"OpenAI request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiCompatibleResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("OpenAI returned an empty/unparseable response.");

        var choice = parsed.Choices.FirstOrDefault()
            ?? throw new ContentGenerationException("OpenAI response contained no choices.");

        return new ChatCompletionResult(
            Content: choice.Message.Content,
            ModelUsed: parsed.Model ?? _options.Model,
            PromptTokens: parsed.Usage?.PromptTokens,
            CompletionTokens: parsed.Usage?.CompletionTokens);
    }
}
