using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

/// <summary>
/// Per-client GeekBackend publish configuration. The API key itself is never stored here —
/// <see cref="ApiKeyEnvVar"/> names a Railway environment variable the publish service reads
/// at call time, so no secrets live in the database.
/// </summary>
public class PublishTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public string GeekBackendApiBaseUrl { get; set; } = string.Empty;
    public string ApiKeyEnvVar { get; set; } = string.Empty;
    public int? DefaultAuthorId { get; set; }
    public CategoryStrategy CategoryStrategy { get; set; } = CategoryStrategy.DepartmentBased;
}
