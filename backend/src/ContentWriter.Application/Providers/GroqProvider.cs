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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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
            MaxTokens = request.MaxOutputTokens,
            // Groq's structured outputs share OpenAI's response_format contract, but strict:true
            // (grammar-constrained, guaranteed schema compliance) is only supported on
            // openai/gpt-oss-20b/120b — not this project's configured llama-3.3-70b-versatile.
            // strict:false ("best-effort") is what's actually available on the configured model:
            // the schema is a strong hint, not a hard guarantee. If the configured Groq model
            // changes to one of the gpt-oss variants, flip this to true to match OpenAiProvider.
            ResponseFormat = request.JsonSchema is null
                ? null
                : new OpenAiResponseFormat
                {
                    JsonSchema = new OpenAiJsonSchemaSpec
                    {
                        Name = request.JsonSchemaName ?? "response",
                        Strict = false,
                        Schema = System.Text.Json.Nodes.JsonNode.Parse(request.JsonSchema),
                    },
                },
        };

        // Rate-limit retries only — lower this if you'd rather fail fast than wait out Groq's free-tier TPM cap.
        const int maxAttempts = 2;
        HttpResponseMessage response;
        string body;
        var retryCount = 0;
        string? retryReason = null;

        var attempt = 1;
        while (true)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new ContentGenerationException("Could not reach the Groq API.", ex);
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 15);
                retryCount++;
                retryReason = $"Groq rate limit (429) on attempt {attempt} — waited {delay.TotalSeconds:0}s and retried.";
                _logger.LogWarning(
                    "Groq rate-limited (attempt {Attempt}/{MaxAttempts}) — retrying in {DelaySeconds}s.",
                    attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                attempt++;
                continue;
            }

            break;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Groq returned {Status}: {Body}", response.StatusCode, body);
            throw new ContentGenerationException($"Groq request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiCompatibleResponse>(body, JsonOptions)
            ?? throw new ContentGenerationException("Groq returned an empty/unparseable response.");

        var choice = parsed.Choices.FirstOrDefault()
            ?? throw new ContentGenerationException("Groq response contained no choices.");

        // #region agent log
        try
        {
            var content = choice.Message.Content ?? string.Empty;
            var debugPayload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sessionId"] = "d9194e",
                ["hypothesisId"] = "A",
                ["location"] = "GroqProvider.CompleteAsync:result",
                ["message"] = "groq completion finish_reason",
                ["data"] = new Dictionary<string, object?>
                {
                    ["finishReason"] = choice.FinishReason,
                    ["completionTokens"] = parsed.Usage?.CompletionTokens,
                    ["promptTokens"] = parsed.Usage?.PromptTokens,
                    ["maxTokens"] = request.MaxOutputTokens,
                    ["contentLength"] = content.Length,
                    ["model"] = parsed.Model ?? model,
                },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            File.AppendAllText(
                "/Users/jeffmartin/development/content-writer-v2/.cursor/debug-d9194e.log",
                debugPayload + "\n");
        }
        catch { /* debug only */ }
        // #endregion

        return new ChatCompletionResult(
            Content: choice.Message.Content,
            ModelUsed: parsed.Model ?? model,
            PromptTokens: parsed.Usage?.PromptTokens,
            CompletionTokens: parsed.Usage?.CompletionTokens,
            RetryCount: retryCount,
            RetryReason: retryReason);
    }
}
