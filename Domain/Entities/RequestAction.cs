using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain.Entities;

public sealed class RequestAction
{
    public long Id { get; set; }
    public int WorkflowRequestId { get; set; }
    public WorkflowRequest WorkflowRequest { get; set; } = null!;
    public int WorkflowDefinitionId { get; set; }
    public int? WorkflowStepId { get; set; }
    public WorkflowStep? WorkflowStep { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public ApplicationUser Actor { get; set; } = null!;
    public RequestActionType ActionType { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
