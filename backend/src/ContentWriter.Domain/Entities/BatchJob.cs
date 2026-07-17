using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

public class BatchJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public BatchJobStatus Status { get; set; } = BatchJobStatus.Queued;
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public int FailedItems { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public List<BatchJobItem> Items { get; set; } = new();
}
