using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain.Entities;

public sealed class WorkflowRequest
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser Requester { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Department { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Draft;
    public int? CurrentStepId { get; set; }
    public WorkflowStep? CurrentStep { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<RequestAction> Actions { get; set; } = new List<RequestAction>();
}
