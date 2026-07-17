namespace ContentWriter.Domain.Entities;

public class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PublishTarget? PublishTarget { get; set; }
    public List<Project> Projects { get; set; } = new();
}
