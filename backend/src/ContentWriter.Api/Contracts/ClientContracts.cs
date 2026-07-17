using ContentWriter.Domain.Enums;

namespace ContentWriter.Api.Contracts;

public record CreateClientRequest(string Name, string? Notes);

public record CreatePublishTargetRequest(
    string GeekBackendApiBaseUrl, string ApiKeyEnvVar, int? DefaultAuthorId, CategoryStrategy CategoryStrategy);

public record PublishTargetResponse(
    Guid Id, string GeekBackendApiBaseUrl, string ApiKeyEnvVar, int? DefaultAuthorId, CategoryStrategy CategoryStrategy);

public record ClientResponse(Guid Id, string Name, string? Notes, DateTime CreatedAtUtc, PublishTargetResponse? PublishTarget);
