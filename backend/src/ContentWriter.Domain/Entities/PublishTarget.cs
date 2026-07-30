using System.Text.Json.Serialization;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

/// <summary>
/// Per-client GeekBackend publish configuration. OAuth2 client-credentials secrets are never stored
/// here — <see cref="ClientIdEnvVar"/> and <see cref="ClientSecretEnvVar"/> name Railway environment
/// variables the publish service reads at call time, so no secrets live in the database.
/// </summary>
public class PublishTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }

    /// <summary>Back-reference to the owning row; not serialized (ClientId is the durable FK) — a populated value here forms a JSON cycle through Client.PublishTarget.</summary>
    [JsonIgnore]
    public Client? Client { get; set; }

    public string GeekBackendApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Relative path (under <see cref="GeekBackendApiBaseUrl"/>) of GeekBackend's OAuth2 token endpoint, e.g. "api/oauth/token".</summary>
    public string OAuthTokenEndpoint { get; set; } = "api/oauth/token";
    public string ClientIdEnvVar { get; set; } = string.Empty;
    public string ClientSecretEnvVar { get; set; } = string.Empty;

    public int? DefaultAuthorId { get; set; }
    public CategoryStrategy CategoryStrategy { get; set; } = CategoryStrategy.DepartmentBased;
}
