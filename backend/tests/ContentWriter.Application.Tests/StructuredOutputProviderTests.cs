using System.Net;
using System.Text;
using System.Text.Json;
using ContentWriter.Application.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContentWriter.Application.Tests;

/// <summary>Confirms each provider actually sends provider-native structured-output enforcement
/// when a JsonSchema is supplied, and leaves the request unchanged (today's behavior) when it
/// isn't — the specific gap found this session (the fields existed on ChatCompletionRequest but no
/// provider ever read them).</summary>
public class StructuredOutputProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }
        private readonly string _responseJson;

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private const string SimpleSchema = """{"type":"object","additionalProperties":false,"required":["heading"],"properties":{"heading":{"type":"string"}}}""";

    [Fact]
    public async Task OpenAiProvider_WithJsonSchema_SendsResponseFormatStrictTrue()
    {
        var handler = new CapturingHandler(
            """{"model":"gpt-4o","choices":[{"message":{"role":"assistant","content":"{}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
        var client = new HttpClient(handler);
        var options = Options.Create(new LlmProvidersOptions { OpenAi = new OpenAiOptions { ApiKey = "test-key" } });
        var provider = new OpenAiProvider(client, options, NullLogger<OpenAiProvider>.Instance);

        await provider.CompleteAsync(new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.User, "hi")],
            JsonSchemaName: "section",
            JsonSchema: SimpleSchema));

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var responseFormat = doc.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var jsonSchema = responseFormat.GetProperty("json_schema");
        Assert.Equal("section", jsonSchema.GetProperty("name").GetString());
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", jsonSchema.GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAiProvider_WithoutJsonSchema_OmitsResponseFormat()
    {
        var handler = new CapturingHandler(
            """{"model":"gpt-4o","choices":[{"message":{"role":"assistant","content":"hi"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
        var client = new HttpClient(handler);
        var options = Options.Create(new LlmProvidersOptions { OpenAi = new OpenAiOptions { ApiKey = "test-key" } });
        var provider = new OpenAiProvider(client, options, NullLogger<OpenAiProvider>.Instance);

        await provider.CompleteAsync(new ChatCompletionRequest(Messages: [new ChatMessage(ChatRole.User, "hi")]));

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task GroqProvider_WithJsonSchema_SendsResponseFormatStrictFalse()
    {
        // strict:false, not true — Groq's structured outputs only guarantee strict:true on
        // openai/gpt-oss-20b/120b, not this project's configured llama-3.3-70b-versatile.
        var handler = new CapturingHandler(
            """{"model":"llama-3.3-70b-versatile","choices":[{"message":{"role":"assistant","content":"{}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
        var client = new HttpClient(handler);
        var options = Options.Create(new LlmProvidersOptions { Groq = new GroqOptions { ApiKey = "test-key" } });
        var provider = new GroqProvider(client, options, NullLogger<GroqProvider>.Instance);

        await provider.CompleteAsync(new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.User, "hi")],
            JsonSchemaName: "section",
            JsonSchema: SimpleSchema));

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var jsonSchema = doc.RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.False(jsonSchema.GetProperty("strict").GetBoolean());
    }

    [Fact]
    public async Task AnthropicProvider_WithJsonSchema_SendsForcedToolUse_AndReadsToolInputBack()
    {
        var handler = new CapturingHandler(
            """{"model":"claude-sonnet-5","content":[{"type":"tool_use","name":"section","input":{"heading":"Hello"}}],"usage":{"input_tokens":1,"output_tokens":1}}""");
        var client = new HttpClient(handler);
        var options = Options.Create(new LlmProvidersOptions { Anthropic = new AnthropicOptions { ApiKey = "test-key" } });
        var provider = new AnthropicProvider(client, options, NullLogger<AnthropicProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.User, "hi")],
            JsonSchemaName: "section",
            JsonSchema: SimpleSchema));

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var tool = doc.RootElement.GetProperty("tools")[0];
        Assert.Equal("section", tool.GetProperty("name").GetString());
        var toolChoice = doc.RootElement.GetProperty("tool_choice");
        Assert.Equal("tool", toolChoice.GetProperty("type").GetString());
        Assert.Equal("section", toolChoice.GetProperty("name").GetString());

        // The parsed tool_use "input" round-trips back as the Content string, ready for
        // LlmResponseJsonParser to deserialize as if it were free-text JSON.
        Assert.Contains("\"heading\":\"Hello\"", result.Content);
    }

    [Fact]
    public async Task AnthropicProvider_WithoutJsonSchema_UsesPlainTextContentBlock()
    {
        var handler = new CapturingHandler(
            """{"model":"claude-sonnet-5","content":[{"type":"text","text":"hello world"}],"usage":{"input_tokens":1,"output_tokens":1}}""");
        var client = new HttpClient(handler);
        var options = Options.Create(new LlmProvidersOptions { Anthropic = new AnthropicOptions { ApiKey = "test-key" } });
        var provider = new AnthropicProvider(client, options, NullLogger<AnthropicProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(Messages: [new ChatMessage(ChatRole.User, "hi")]));

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("tools", out _));
        Assert.Equal("hello world", result.Content);
    }
}
