using ContentWriter.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Providers;

public interface IContentProviderFactory
{
    IContentGenerationProvider Get(LlmProviderType providerType);
    IContentGenerationProvider GetDefault();
}

/// <summary>
/// Resolves the correct <see cref="IContentGenerationProvider"/> implementation using keyed DI
/// registrations. This is the seam that lets the orchestrator swap LM Studio / OpenAI / Anthropic
/// per-project (Project.PreferredProvider) without an if/else chain anywhere else in the app.
/// </summary>
public class ContentProviderFactory : IContentProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LlmProvidersOptions _options;

    public ContentProviderFactory(IServiceProvider serviceProvider, IOptions<LlmProvidersOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public IContentGenerationProvider Get(LlmProviderType providerType) =>
        _serviceProvider.GetRequiredKeyedService<IContentGenerationProvider>(providerType);

    public IContentGenerationProvider GetDefault()
    {
        var defaultType = Enum.TryParse<LlmProviderType>(_options.DefaultProvider, out var parsed)
            ? parsed
            : LlmProviderType.LmStudio;

        return Get(defaultType);
    }
}
