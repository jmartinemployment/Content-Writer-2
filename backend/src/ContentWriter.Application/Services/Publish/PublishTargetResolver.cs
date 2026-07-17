using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services.Publish;

/// <summary>
/// Resolves a <see cref="Client"/>'s <see cref="PublishTarget"/> into connection details for a
/// GeekBackend call. The API key is never stored — <see cref="PublishTarget.ApiKeyEnvVar"/> names
/// the Railway environment variable holding it, read fresh on every call.
/// </summary>
public static class PublishTargetResolver
{
    public static PublishTargetContext Resolve(Client client, bool requireAuthorId = true)
    {
        var publishTarget = client.PublishTarget
            ?? throw new ContentGenerationException(
                $"Client '{client.Name}' has no PublishTarget configured — cannot publish.");

        if (requireAuthorId && publishTarget.DefaultAuthorId is null)
        {
            throw new ContentGenerationException(
                $"PublishTarget for client '{client.Name}' has no DefaultAuthorId configured. " +
                "geek_blog.posts.author_id is required — set it to a seeded geek_blog.users.id before publishing.");
        }

        var apiKey = Environment.GetEnvironmentVariable(publishTarget.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ContentGenerationException(
                $"Environment variable '{publishTarget.ApiKeyEnvVar}' (PublishTarget.ApiKeyEnvVar for client " +
                $"'{client.Name}') is not set — cannot call GeekBackend without an API key.");
        }

        return new PublishTargetContext(publishTarget.GeekBackendApiBaseUrl, apiKey, publishTarget.DefaultAuthorId);
    }
}
