using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services.Publish;

/// <summary>
/// Resolves a <see cref="Client"/>'s <see cref="PublishTarget"/> into connection details for a
/// GeekBackend call. The OAuth2 client secret is never stored — <see cref="PublishTarget.ClientIdEnvVar"/>
/// and <see cref="PublishTarget.ClientSecretEnvVar"/> name Railway environment variables holding the
/// client-credentials, read fresh on every call.
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

        var oauthClientId = Environment.GetEnvironmentVariable(publishTarget.ClientIdEnvVar);
        if (string.IsNullOrWhiteSpace(oauthClientId))
        {
            throw new ContentGenerationException(
                $"Environment variable '{publishTarget.ClientIdEnvVar}' (PublishTarget.ClientIdEnvVar for client " +
                $"'{client.Name}') is not set — cannot authenticate to GeekBackend without an OAuth client id.");
        }

        var oauthClientSecret = Environment.GetEnvironmentVariable(publishTarget.ClientSecretEnvVar);
        if (string.IsNullOrWhiteSpace(oauthClientSecret))
        {
            throw new ContentGenerationException(
                $"Environment variable '{publishTarget.ClientSecretEnvVar}' (PublishTarget.ClientSecretEnvVar for client " +
                $"'{client.Name}') is not set — cannot authenticate to GeekBackend without an OAuth client secret.");
        }

        var tokenEndpoint = BuildAbsoluteUri(publishTarget.GeekBackendApiBaseUrl, publishTarget.OAuthTokenEndpoint);
        var oauth = new GeekOAuthCredentials(tokenEndpoint, oauthClientId, oauthClientSecret);

        return new PublishTargetContext(publishTarget.GeekBackendApiBaseUrl, oauth, publishTarget.DefaultAuthorId);
    }

    private static string BuildAbsoluteUri(string baseUrl, string relativePath) =>
        new Uri(baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/')).ToString();
}
