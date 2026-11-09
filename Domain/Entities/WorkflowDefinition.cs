namespace MiniWorkflowEngine.Domain.Entities;

public sealed class WorkflowDefinition
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? GraphJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}
