using ContentWriter.Domain.Enums;

namespace ContentWriter.Api.Contracts;

public record CreateBatchJobRequest(
    Guid ClientId, List<CreateBatchJobItemRequest> Items);

public record CreateBatchJobItemRequest(string TargetKeyword, string ProjectUrl, LlmProviderType? PreferredProvider);

public record BatchStepResponse(
    BatchStepName StepName, BatchStepStatus Status, int AttemptCount, string? ErrorText,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc);

public record BatchJobItemResponse(
    Guid Id, Guid? ProjectId, string TargetKeyword, BatchJobItemStatus Status, string? ErrorText,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, List<BatchStepResponse> Steps);

public record BatchJobResponse(
    Guid Id, Guid ClientId, BatchJobStatus Status, int TotalItems, int CompletedItems, int FailedItems,
    DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, List<BatchJobItemResponse> Items);

public record BatchJobSummaryResponse(
    Guid Id, Guid ClientId, BatchJobStatus Status, int TotalItems, int CompletedItems, int FailedItems,
    DateTime CreatedAtUtc);
