using System.Net.Http.Json;
using System.Text.Json;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Providers;

/// <summary>
/// Talks to a local LM Studio server exposing the OpenAI-compatible
/// /v1/chat/completions endpoint (default: http://localhost:1234/v1/chat/completions).
/// </summary>
public class LmStudioProvider : IContentGenerationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly LmStudioOptions _options;
    private readonly ILogger<LmStudioProvider> _logger;
    private string? _resolvedModel;

    public LlmProviderType ProviderType => LlmProviderType.LmStudio;

    public LmStudioProvider(HttpClient httpClient, IOptions<LlmProvidersOptions> options, ILogger<LmStudioProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.LmStudio;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<LmStudioHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var modelsUrl = GetModelsUrl();
            using var response = await _httpClient.GetAsync(modelsUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return new LmStudioHealthStatus(
                    false,
                    null,
                    $"LM Studio responded with {(int)response.StatusCode}: {body}");
            }

            var modelId = await ResolveModelAsync(cancellationToken);
            return new LmStudioHealthStatus(true, modelId, "LM Studio is reachable.");
        }
        catch (HttpRequestException)
        {
            return new LmStudioHealthStatus(
                false,
                null,
                $"Could not reach LM Studio at {_options.BaseUrl}. Start LM Studio, load a model, and enable the local server.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LmStudioHealthStatus(false, null, $"LM Studio timed out after {_options.TimeoutSeconds}s.");
        }
    }

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var model = request.Model ?? await ResolveModelAsync(cancellationToken);
        var payload = new OpenAiCompatibleRequest
        {
            Model = model,
            Messages = request.Messages.Select(m => new OpenAiCompatibleMessage(m.RoleString, m.Content)).ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxOutputTokens
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(_options.BaseUrl, payload, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException(
                $"Could not reach LM Studio at {_options.BaseUrl}. Is LM Studio running with a model loaded and the local server enabled?", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ContentGenerationException(
                $"LM Studio request timed out after {_options.TimeoutSeconds}s. Try a smaller prompt or increase LlmProviders:LmStudio:TimeoutSeconds.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("LM Studio returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"LM Studio request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiCompatibleResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("LM Studio returned an empty/unparseable response.");

        var choice = parsed.Choices.FirstOrDefault()
            ?? throw new ContentGenerationException("LM Studio response contained no choices.");

        return new ChatCompletionResult(
            Content: choice.Message.Content,
            ModelUsed: parsed.Model ?? model,
            PromptTokens: parsed.Usage?.PromptTokens,
            CompletionTokens: parsed.Usage?.CompletionTokens);
    }

    private async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedModel))
        {
            return _resolvedModel;
        }

        if (!string.IsNullOrWhiteSpace(_options.Model) && !string.Equals(_options.Model, "local-model", StringComparison.Ordinal))
        {
            _resolvedModel = _options.Model;
            return _resolvedModel;
        }

        try
        {
            var modelsUrl = GetModelsUrl();
            using var response = await _httpClient.GetAsync(modelsUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ContentGenerationException($"LM Studio /v1/models failed ({(int)response.StatusCode}): {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<LmStudioModelsResponse>(JsonOptions, cancellationToken)
                ?? throw new ContentGenerationException("LM Studio /v1/models returned an empty response.");

            var modelId = payload.Data.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Id))?.Id
                ?? throw new ContentGenerationException("LM Studio has no loaded model. Load a model in LM Studio before generating content.");

            _resolvedModel = modelId;
            _logger.LogInformation("Resolved LM Studio model to {ModelId}", modelId);
            return modelId;
        }
        catch (HttpRequestException ex)
        {
            throw new ContentGenerationException(
                $"Could not reach LM Studio at {_options.BaseUrl}. Start LM Studio, load a model, and enable the local server.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ContentGenerationException(
                $"LM Studio model lookup timed out after {_options.TimeoutSeconds}s.", ex);
        }
    }

    private string GetModelsUrl()
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/chat/completions".Length];
        }

        return $"{baseUrl}/models";
    }

    private sealed class LmStudioModelsResponse
    {
        public List<LmStudioModel> Data { get; set; } = new();
    }

    private sealed class LmStudioModel
    {
        public string Id { get; set; } = string.Empty;
    }
}
