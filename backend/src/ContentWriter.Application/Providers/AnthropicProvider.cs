using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Providers;

/// <summary>Talks to the Anthropic Messages API (https://api.anthropic.com/v1/messages).</summary>
public class AnthropicProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicProvider> _logger;

    public LlmProviderType ProviderType => LlmProviderType.Anthropic;

    public AnthropicProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Anthropic;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ContentGenerationException("Anthropic API key is not configured (LlmProviders:Anthropic:ApiKey).");
        }

        // Anthropic's Messages API takes system prompts as a top-level field, not a message with role "system".
        var systemPrompt = string.Join("\n\n", request.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content));

        var turnMessages = request.Messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new AnthropicMessage(m.RoleString, m.Content))
            .ToList();

        var payload = new AnthropicRequest
        {
            Model = request.Model ?? _options.Model,
            System = string.IsNullOrEmpty(systemPrompt) ? null : systemPrompt,
            Messages = turnMessages,
            MaxTokens = request.MaxOutputTokens,
            Temperature = request.Temperature
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", _options.AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException("Could not reach the Anthropic API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"Anthropic request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("Anthropic returned an empty/unparseable response.");

        var textBlock = parsed.Content.FirstOrDefault(c => c.Type == "text")
            ?? throw new ContentGenerationException("Anthropic response contained no text content block.");

        return new ChatCompletionResult(
            Content: textBlock.Text ?? string.Empty,
            ModelUsed: parsed.Model ?? _options.Model,
            PromptTokens: parsed.Usage?.InputTokens,
            CompletionTokens: parsed.Usage?.OutputTokens);
    }

    private sealed class AnthropicRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public List<AnthropicMessage> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
    }

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public List<AnthropicContentBlock> Content { get; set; } = new();
        [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
