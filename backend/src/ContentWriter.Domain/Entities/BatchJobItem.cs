using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

/// <summary>One target keyword within a BatchJob. Owns a Project once the pipeline creates it.</summary>
public class BatchJobItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchJobId { get; set; }
    public BatchJob? BatchJob { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string TargetKeyword { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public LlmProviderType PreferredProvider { get; set; } = LlmProviderType.LmStudio;

    public BatchJobItemStatus Status { get; set; } = BatchJobItemStatus.Queued;
    public string? ErrorText { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public List<BatchJobItemStep> Steps { get; set; } = new();
}
