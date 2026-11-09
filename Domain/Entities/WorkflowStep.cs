using Microsoft.AspNetCore.Identity;

namespace MiniWorkflowEngine.Domain.Entities;

public sealed class WorkflowStep
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public Guid? GraphNodeId { get; set; }
    public string? AssignedUserId { get; set; }
    public ApplicationUser? AssignedUser { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string ApproverRoleId { get; set; } = string.Empty;
    public IdentityRole ApproverRole { get; set; } = null!;
    public ICollection<StepCondition> Conditions { get; set; } = new List<StepCondition>();
}
