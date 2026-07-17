using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

/// <summary>
/// Per-step progress for a BatchJobItem. Drives the dashboard's step status display and the live
/// revision counter (AttemptCount) for the Review/word-count-regen loops. NOT the source of truth
/// for resumability — the worker re-derives "is this step actually done" from the real domain data
/// (CrawledSite, GeneratedContents row completeness) so a crash mid-write can't leave a stale
/// Completed status that causes resume to silently skip a truncated step.
/// </summary>
public class BatchJobItemStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchJobItemId { get; set; }
    public BatchJobItem? BatchJobItem { get; set; }

    public BatchStepName StepName { get; set; }
    public BatchStepStatus Status { get; set; } = BatchStepStatus.Pending;
    public int AttemptCount { get; set; }
    public string? ErrorText { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
