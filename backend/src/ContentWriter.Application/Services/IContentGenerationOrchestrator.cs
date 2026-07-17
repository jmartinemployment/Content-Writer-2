using ContentWriter.Application.DTOs;

namespace ContentWriter.Application.Services;

public interface IContentGenerationOrchestrator
{
    Task<GeneratedContentSet> GeneratePillarPlanAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GeneratePillarBodyAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GeneratePillarAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateToolPagesAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateBlogAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateSocialAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateColdOutreachAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateImagePromptsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateAllAsync(Guid projectId, CancellationToken cancellationToken = default);
}
